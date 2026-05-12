using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities.Discovery.Bundles;
using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Api.Features.Presets.Apply.Layers;

/// <summary>
/// Pro Services rollout (Artifact 5 §3.7, §4) — applies a
/// <see cref="WorkflowDefinitionBundle"/> to the install's
/// <c>workflow_definitions</c> table. Each (<see cref="WorkflowDefinition.EntityType"/>,
/// <see cref="WorkflowDefinition.DefinitionId"/>) pair from the bundle is
/// upserted; existing in-flight runs continue on the previous version
/// (per WorkflowDefinition's versioned-id convention).
///
/// <para>Conflict policy is intentionally simple here — the bundle is
/// keyed by entity type, but the entity might already have multiple
/// versioned definitions. The applier:</para>
/// <list type="bullet">
///   <item>Computes a <see cref="WorkflowDefinition.DefinitionId"/> for
///   each entry as <c>"{entityType-lower}-preset-{presetId-lower}-v1"</c>.</item>
///   <item>Inserts if no matching DefinitionId exists.</item>
///   <item>Updates the row's StepsJson if the DefinitionId already exists
///   AND the StepsJson differs (treated as a re-seed; the row's
///   <see cref="WorkflowDefinition.IsSeedData"/> flag must be true to be
///   eligible for update — admin-edited rows are skipped).</item>
/// </list>
///
/// <para>This intentionally keeps the bundle's role narrow: shipping a
/// seed definition that runs out-of-the-box. Custom workflow versioning
/// remains the user's job via the WorkflowDefinitions admin surface.</para>
///
/// <para>Caller is responsible for <c>SaveChangesAsync</c>.</para>
/// </summary>
public static class WorkflowDefinitionBundleApplier
{
    public static async Task<LayerApplyResult> ApplyAsync(
        WorkflowDefinitionBundle bundle,
        AppDbContext db,
        string presetId,
        CancellationToken cancellationToken)
    {
        var added = 0;
        var updated = 0;
        var skipped = 0;

        foreach (var (entityType, stepsJson) in bundle.DefinitionsByEntityType)
        {
            var definitionId = BuildDefinitionId(entityType, presetId);

            var existing = await db.WorkflowDefinitions
                .FirstOrDefaultAsync(w => w.DefinitionId == definitionId, cancellationToken);

            if (existing is null)
            {
                db.WorkflowDefinitions.Add(new WorkflowDefinition
                {
                    DefinitionId = definitionId,
                    EntityType = entityType,
                    DefaultMode = "guided",
                    StepsJson = stepsJson,
                    IsSeedData = true,
                });
                added++;
                continue;
            }

            // Existing row — only re-seed if it's marked seed-data (admin
            // edits stay protected).
            if (!existing.IsSeedData)
            {
                skipped++;
                continue;
            }

            if (existing.StepsJson == stepsJson)
            {
                skipped++;
                continue;
            }

            existing.StepsJson = stepsJson;
            updated++;
        }

        return new LayerApplyResult(
            Layer: PresetBundleLayer.WorkflowDefinition,
            AddedCount: added,
            UpdatedCount: updated,
            SkippedCount: skipped);
    }

    private static string BuildDefinitionId(string entityType, string presetId)
    {
        // entityType is typed (e.g. "Job"); presetId is "PRESET-08".
        // Result: "job-preset-08-v1" — stable, sortable, recognizable.
        var et = entityType.ToLowerInvariant();
        var pid = presetId.ToLowerInvariant().Replace("preset-", string.Empty);
        return $"{et}-preset-{pid}-v1";
    }
}
