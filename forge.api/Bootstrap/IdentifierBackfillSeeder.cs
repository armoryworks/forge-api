using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Bootstrap;

/// <summary>
/// Idempotent boot backfill: gives every existing record an active row in the business-identifier
/// registry from its current number, so a rename can preserve history and old numbers resolve. Runs
/// on every boot (a fresh install has nothing to seed; a populated one is seeded once, then no-ops).
/// Extended per entity as each is wired to the registry.
/// </summary>
public interface IIdentifierBackfillSeeder
{
    Task SeedAsync(CancellationToken ct = default);
}

public class IdentifierBackfillSeeder(
    AppDbContext db,
    IBusinessIdentifierService identifiers,
    ILogger<IdentifierBackfillSeeder> logger) : IIdentifierBackfillSeeder
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        var seeded = 0;

        // Parts — any part without an active identifier yet.
        var parts = await db.Parts.AsNoTracking()
            .Where(p => !db.BusinessIdentifiers.Any(b =>
                b.EntityType == BusinessEntityType.Part && b.EntityId == p.Id && b.EffectiveTo == null))
            .Select(p => new { p.Id, p.PartNumber })
            .ToListAsync(ct);
        foreach (var p in parts)
        {
            try
            {
                await identifiers.IssueAsync(BusinessEntityType.Part, p.Id, p.PartNumber, ct);
                seeded++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[IDENTIFIER-BACKFILL] Skipped Part {PartId} ({Number})", p.Id, p.PartNumber);
            }
        }

        if (seeded > 0)
            logger.LogInformation("[IDENTIFIER-BACKFILL] Seeded {Count} identifier row(s).", seeded);
    }
}
