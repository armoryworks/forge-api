using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Api.Capabilities;

/// <summary>
/// Phase 4 Phase-A — Idempotent seeder for the capabilities table.
///
/// Stable-ID upsert pattern (4F decision #4):
///   • New IDs (not yet in DB) are INSERTed with Enabled = IsDefaultOn.
///   • Existing rows: only metadata (Name, Description, Area, IsDefaultOn,
///     RequiresRoles) is refreshed. Enabled is NEVER overwritten — admin
///     state is operator-owned.
///
/// Runs at startup after EF migrations and before the snapshot is hydrated.
/// </summary>
public interface ICapabilityCatalogSeeder
{
    Task SeedAsync(CancellationToken ct = default);
}

public class CapabilityCatalogSeeder(AppDbContext db, ILogger<CapabilityCatalogSeeder> logger) : ICapabilityCatalogSeeder
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        // Suppress audit during seed — these are system-owned rows.
        var prevSuppress = db.SuppressAudit;
        db.SuppressAudit = true;
        try
        {
            var existing = await db.Capabilities.ToDictionaryAsync(c => c.Code, ct);
            var inserted = 0;
            var refreshed = 0;

            foreach (var def in CapabilityCatalog.All)
            {
                if (existing.TryGetValue(def.Code, out var row))
                {
                    // Refresh metadata only; never touch Enabled.
                    var changed = false;
                    if (row.Area != def.Area) { row.Area = def.Area; changed = true; }
                    if (row.Name != def.Name) { row.Name = def.Name; changed = true; }
                    if (row.Description != def.Description) { row.Description = def.Description; changed = true; }
                    if (row.IsDefaultOn != def.IsDefaultOn) { row.IsDefaultOn = def.IsDefaultOn; changed = true; }
                    if (row.RequiresRoles != def.RequiresRoles) { row.RequiresRoles = def.RequiresRoles; changed = true; }
                    if (changed) refreshed++;
                }
                else
                {
                    db.Capabilities.Add(new Capability
                    {
                        Code = def.Code,
                        Area = def.Area,
                        Name = def.Name,
                        Description = def.Description,
                        Enabled = def.IsDefaultOn,
                        IsDefaultOn = def.IsDefaultOn,
                        RequiresRoles = def.RequiresRoles,
                    });
                    inserted++;
                }
            }

            if (inserted > 0 || refreshed > 0)
            {
                await db.SaveChangesAsync(ct);
            }

            logger.LogInformation(
                "[CAPABILITY-SEED] Catalog seed complete: inserted={Inserted}, refreshed={Refreshed}, total={Total}",
                inserted, refreshed, CapabilityCatalog.All.Count);

            // Opt-in override: SEED_ENABLE_CAPABILITIES is an explicit,
            // comma-separated list of capability codes to force-enable after the
            // catalog seed. Used by the e2e / demo stack to exercise
            // off-by-default features (e.g. CAP-O2C-LEAD, CAP-EXT-ANNOUNCEMENTS)
            // without an admin toggling them by hand. It is absent in normal
            // installs, so the "Enabled is operator-owned" rule above still holds
            // everywhere the var isn't set. (Catalog rows are saved above, so the
            // just-inserted rows are queryable here.)
            var enableCsv = Environment.GetEnvironmentVariable("SEED_ENABLE_CAPABILITIES");
            if (!string.IsNullOrWhiteSpace(enableCsv))
            {
                var codes = enableCsv.Split(
                    ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var forced = 0;
                foreach (var code in codes)
                {
                    var row = await db.Capabilities.FirstOrDefaultAsync(c => c.Code == code, ct);
                    if (row is null)
                    {
                        logger.LogWarning(
                            "[CAPABILITY-SEED] SEED_ENABLE_CAPABILITIES lists unknown code '{Code}' — skipped.", code);
                        continue;
                    }
                    if (!row.Enabled) { row.Enabled = true; forced++; }
                }
                if (forced > 0) await db.SaveChangesAsync(ct);
                logger.LogInformation(
                    "[CAPABILITY-SEED] SEED_ENABLE_CAPABILITIES force-enabled {Forced} capability(ies) from: {Codes}",
                    forced, enableCsv);
            }

            // Phase 4 Phase-C — surface catalog drift (edges referencing
            // codes not in the catalog body). Warns once at startup and
            // then silently skips the bad edges at evaluation time.
            var byCode = CapabilityCatalog.All.ToDictionary(c => c.Code, c => c);
            var dropped = CapabilityDependencyResolver.ValidateGraph(byCode, logger);
            if (dropped > 0)
            {
                logger.LogWarning(
                    "[CAPABILITY-CATALOG] {Dropped} dependency / mutex edge(s) skipped because their endpoints are missing from the catalog. The install remains usable; check CapabilityCatalogRelations for drift.",
                    dropped);
            }
        }
        finally
        {
            db.SuppressAudit = prevSuppress;
        }
    }
}
