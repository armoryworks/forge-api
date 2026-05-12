using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities.Discovery.Bundles;
using Forge.Core.Entities;
using Forge.Data.Context;

// Disambiguate the entity from the `Forge.Api.Features.ReferenceData`
// feature namespace. Without this alias, sibling-namespace resolution
// makes the bare `ReferenceData` token resolve to the namespace.
using ReferenceDataEntity = Forge.Core.Entities.ReferenceData;

namespace Forge.Api.Features.Presets.Apply.Layers;

/// <summary>
/// Pro Services rollout (Artifact 5 §3.2, §4) — applies a
/// <see cref="ReferenceDataBundle"/> to the install's <c>reference_data</c>
/// table. One bundle group corresponds to one <c>group_code</c>;
/// per-value seeds are upserted by (group, code) honoring the policy.
///
/// <para><b>Conflict semantics:</b></para>
/// <list type="bullet">
///   <item><c>UpsertSeed</c> (default): missing values are added, existing
///   values are NOT updated — admin customization wins. New seeded rows
///   carry <c>IsSeedData = true</c>.</item>
///   <item><c>Overwrite</c>: every seeded value is upserted; existing rows
///   have their label / sort-order replaced.</item>
///   <item><c>Skip</c>: if any rows exist in a group, the whole group is
///   skipped — only seed empty groups.</item>
/// </list>
///
/// <para>Caller is responsible for <c>SaveChangesAsync</c>.</para>
/// </summary>
public static class ReferenceDataBundleApplier
{
    public static async Task<LayerApplyResult> ApplyAsync(
        ReferenceDataBundle bundle,
        AppDbContext db,
        string presetId,
        CancellationToken cancellationToken)
    {
        _ = presetId;  // Reserved for future activity-log linkage; ReferenceData has no source_preset_id today.

        var groupCodes = bundle.Groups.Keys.ToList();
        var existing = await db.ReferenceData
            .Where(r => groupCodes.Contains(r.GroupCode))
            .ToListAsync(cancellationToken);

        // Index existing by (GroupCode, Code) for O(1) lookups in the loop.
        var index = existing
            .GroupBy(r => r.GroupCode)
            .ToDictionary(
                g => g.Key,
                g => g.ToDictionary(r => r.Code, r => r, StringComparer.Ordinal),
                StringComparer.Ordinal);

        var added = 0;
        var updated = 0;
        var skipped = 0;

        foreach (var (groupCode, seeds) in bundle.Groups)
        {
            var hasRowsInGroup = index.TryGetValue(groupCode, out var byCode);
            byCode ??= new Dictionary<string, ReferenceDataEntity>(StringComparer.Ordinal);

            // Skip policy short-circuits whole groups that already have rows.
            if (bundle.ConflictPolicy == ReferenceDataConflictPolicy.Skip && hasRowsInGroup && byCode.Count > 0)
            {
                skipped += seeds.Count;
                continue;
            }

            foreach (var seed in seeds)
            {
                if (byCode.TryGetValue(seed.Code, out var row))
                {
                    if (bundle.ConflictPolicy == ReferenceDataConflictPolicy.Overwrite)
                    {
                        if (row.Label != seed.Label || row.SortOrder != seed.SortOrder || row.Metadata != seed.Metadata)
                        {
                            row.Label = seed.Label;
                            row.SortOrder = seed.SortOrder;
                            row.Metadata = seed.Metadata;
                            updated++;
                        }
                        else
                        {
                            skipped++;
                        }
                    }
                    else
                    {
                        // UpsertSeed: row exists, leave admin customization alone.
                        skipped++;
                    }
                }
                else
                {
                    db.ReferenceData.Add(new ReferenceDataEntity
                    {
                        GroupCode = groupCode,
                        Code = seed.Code,
                        Label = seed.Label,
                        SortOrder = seed.SortOrder,
                        Metadata = seed.Metadata,
                        IsActive = true,
                        IsSeedData = true,
                    });
                    added++;
                }
            }
        }

        return new LayerApplyResult(
            Layer: PresetBundleLayer.ReferenceData,
            AddedCount: added,
            UpdatedCount: updated,
            SkippedCount: skipped);
    }
}
