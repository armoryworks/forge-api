using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Api.Workflows;

/// <summary>
/// Workflow Pattern Phase 3 — Idempotent seeder for the Workflow Pattern
/// substrate. Mirrors <c>CapabilityCatalogSeeder</c> conventions:
///   • Stable-id upsert. New rows are inserted; existing rows have their
///     metadata refreshed but never their admin-edited content.
///   • <c>IsSeedData=true</c> on every seeded row so admin UI can lock seeds.
///   • Suppresses audit during seed (system-owned writes).
/// </summary>
public interface IWorkflowSubstrateSeeder
{
    Task SeedAsync(CancellationToken ct = default);
}

public class WorkflowSubstrateSeeder(
    AppDbContext db,
    ILogger<WorkflowSubstrateSeeder> logger) : IWorkflowSubstrateSeeder
{
    /// <summary>
    /// Pre-beta: alias workflow definitions seeded under prior versions
    /// (<c>part-assembly-guided-v1</c> and <c>part-raw-material-express-v1</c>)
    /// no longer exist in the canonical seed set. Soft-delete any orphaned
    /// rows so they vanish from the runtime catalog. Once the column-drop
    /// migration has shipped to all environments this cleanup is redundant
    /// and can come out — but it's idempotent and cheap so we leave it.
    /// </summary>
    private static readonly string[] RetiredAliasDefinitionIds =
    [
        "part-assembly-guided-v1",
        "part-raw-material-express-v1",
    ];

    /// <summary>
    /// Each tuple is one entity type's substrate: the readiness validators
    /// it owns + the workflow definitions that reference them. Adding a new
    /// entity to the workflow framework means appending one tuple here and
    /// registering its <c>IWorkflowEntityCreator</c> + <c>IWorkflowFieldApplier</c>
    /// + <c>IEntityReadinessLoader</c> in <c>Program.cs</c>.
    /// </summary>
    private static readonly (string EntityType,
                             Func<IReadOnlyList<WorkflowSeedData.ValidatorSeed>> Validators,
                             Func<IReadOnlyList<WorkflowSeedData.DefinitionSeed>> Definitions)[]
        EntityBundles =
    [
        ("Part",
            static () => WorkflowSeedData.PartReadinessValidators,
            static () => WorkflowSeedData.PartWorkflowDefinitions),
        ("Vendor",
            static () => WorkflowSeedData.VendorReadinessValidators,
            static () => WorkflowSeedData.VendorWorkflowDefinitions),
    ];

    public async Task SeedAsync(CancellationToken ct = default)
    {
        var prevSuppress = db.SuppressAudit;
        db.SuppressAudit = true;
        try
        {
            await CleanupRetiredAliasesAsync(ct);
            foreach (var (entityType, validators, definitions) in EntityBundles)
            {
                await SeedValidatorsAsync(entityType, validators(), ct);
                await SeedDefinitionsAsync(definitions(), ct);
            }
        }
        finally
        {
            db.SuppressAudit = prevSuppress;
        }
    }

    private async Task CleanupRetiredAliasesAsync(CancellationToken ct)
    {
        var orphaned = await db.WorkflowDefinitions
            .Where(d => RetiredAliasDefinitionIds.Contains(d.DefinitionId)
                        && d.IsSeedData
                        && d.DeletedAt == null)
            .ToListAsync(ct);

        if (orphaned.Count == 0) return;

        var now = DateTimeOffset.UtcNow;
        foreach (var row in orphaned)
        {
            row.DeletedAt = now;
        }
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "[WORKFLOW-SEED] Soft-deleted {Count} retired alias definition(s): {Ids}",
            orphaned.Count, string.Join(", ", orphaned.Select(o => o.DefinitionId)));
    }

    private async Task SeedValidatorsAsync(
        string entityType,
        IReadOnlyList<WorkflowSeedData.ValidatorSeed> seeds,
        CancellationToken ct)
    {
        var existing = await db.EntityReadinessValidators
            .IgnoreQueryFilters()
            .Where(v => v.EntityType == entityType)
            .ToDictionaryAsync(v => v.ValidatorId, ct);

        var inserted = 0;
        var refreshed = 0;
        foreach (var seed in seeds)
        {
            if (existing.TryGetValue(seed.ValidatorId, out var row))
            {
                // Reactivate soft-deleted seed if needed.
                if (row.DeletedAt is not null)
                {
                    row.DeletedAt = null;
                    row.DeletedBy = null;
                }
                var changed = false;
                if (row.Predicate != seed.Predicate) { row.Predicate = seed.Predicate; changed = true; }
                if (row.DisplayNameKey != seed.DisplayNameKey) { row.DisplayNameKey = seed.DisplayNameKey; changed = true; }
                if (row.MissingMessageKey != seed.MissingMessageKey) { row.MissingMessageKey = seed.MissingMessageKey; changed = true; }
                if (!row.IsSeedData) { row.IsSeedData = true; changed = true; }
                if (changed) refreshed++;
            }
            else
            {
                db.EntityReadinessValidators.Add(new EntityReadinessValidator
                {
                    EntityType = entityType,
                    ValidatorId = seed.ValidatorId,
                    Predicate = seed.Predicate,
                    DisplayNameKey = seed.DisplayNameKey,
                    MissingMessageKey = seed.MissingMessageKey,
                    IsSeedData = true,
                });
                inserted++;
            }
        }

        if (inserted > 0 || refreshed > 0)
            await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "[WORKFLOW-SEED] {EntityType} validators: inserted={Inserted}, refreshed={Refreshed}",
            entityType, inserted, refreshed);
    }

    private async Task SeedDefinitionsAsync(
        IReadOnlyList<WorkflowSeedData.DefinitionSeed> seeds,
        CancellationToken ct)
    {
        var ids = seeds.Select(d => d.DefinitionId).ToList();
        var existing = await db.WorkflowDefinitions
            .IgnoreQueryFilters()
            .Where(d => ids.Contains(d.DefinitionId))
            .ToDictionaryAsync(d => d.DefinitionId, ct);

        var inserted = 0;
        var refreshed = 0;
        foreach (var seed in seeds)
        {
            if (existing.TryGetValue(seed.DefinitionId, out var row))
            {
                if (row.DeletedAt is not null)
                {
                    row.DeletedAt = null;
                    row.DeletedBy = null;
                }
                var changed = false;
                if (row.EntityType != seed.EntityType) { row.EntityType = seed.EntityType; changed = true; }
                if (row.DefaultMode != seed.DefaultMode) { row.DefaultMode = seed.DefaultMode; changed = true; }
                if (row.StepsJson != seed.StepsJson) { row.StepsJson = seed.StepsJson; changed = true; }
                if (row.ExpressTemplateComponent != seed.ExpressTemplateComponent)
                { row.ExpressTemplateComponent = seed.ExpressTemplateComponent; changed = true; }
                if (!row.IsSeedData) { row.IsSeedData = true; changed = true; }
                if (changed) refreshed++;
            }
            else
            {
                db.WorkflowDefinitions.Add(new WorkflowDefinition
                {
                    DefinitionId = seed.DefinitionId,
                    EntityType = seed.EntityType,
                    DefaultMode = seed.DefaultMode,
                    StepsJson = seed.StepsJson,
                    ExpressTemplateComponent = seed.ExpressTemplateComponent,
                    IsSeedData = true,
                });
                inserted++;
            }
        }

        if (inserted > 0 || refreshed > 0)
            await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "[WORKFLOW-SEED] Definitions ({EntityType}): inserted={Inserted}, refreshed={Refreshed}",
            seeds.FirstOrDefault()?.EntityType ?? "?", inserted, refreshed);
    }
}
