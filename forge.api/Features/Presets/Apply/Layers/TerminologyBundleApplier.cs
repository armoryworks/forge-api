using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities.Discovery.Bundles;
using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Api.Features.Presets.Apply.Layers;

/// <summary>
/// Pro Services rollout (Artifact 5 §3.1, §4) — applies a
/// <see cref="TerminologyBundle"/> to the install's <c>terminology_entries</c>
/// table. Each label is upserted by key, honoring the bundle's
/// <see cref="TerminologyConflictPolicy"/>.
///
/// <para><b>Conflict semantics:</b></para>
/// <list type="bullet">
///   <item><c>SkipAdminEdited</c> (default): rows with <c>IsAdminEdited=true</c>
///   are left untouched even if the bundle would otherwise rewrite them.</item>
///   <item><c>Overwrite</c>: every key from the bundle is written, regardless
///   of admin-edit flag. Use for first-time apply / migration.</item>
///   <item><c>Prompt</c>: behaves like <c>SkipAdminEdited</c> but every
///   skipped row is counted as a conflict for caller resolution.</item>
/// </list>
///
/// <para>Caller is responsible for <c>SaveChangesAsync</c>. The applier
/// only stages changes on the supplied <see cref="AppDbContext"/>.</para>
/// </summary>
public static class TerminologyBundleApplier
{
    public static async Task<LayerApplyResult> ApplyAsync(
        TerminologyBundle bundle,
        AppDbContext db,
        string presetId,
        CancellationToken cancellationToken)
    {
        // Pull the current rows for the keys the bundle touches. One query
        // by-keys-list avoids loading the whole table.
        var keys = bundle.Labels.Keys.ToList();
        var existing = await db.TerminologyEntries
            .Where(e => keys.Contains(e.Key))
            .ToDictionaryAsync(e => e.Key, e => e, cancellationToken);

        var added = 0;
        var updated = 0;
        var skipped = 0;
        var conflicted = 0;

        foreach (var (key, label) in bundle.Labels)
        {
            if (existing.TryGetValue(key, out var row))
            {
                // Existing row — apply conflict policy.
                var isProtected = row.IsAdminEdited;
                var policySkip = bundle.ConflictPolicy switch
                {
                    TerminologyConflictPolicy.Overwrite => false,
                    TerminologyConflictPolicy.SkipAdminEdited => isProtected,
                    TerminologyConflictPolicy.Prompt => isProtected,
                    _ => isProtected
                };

                if (policySkip)
                {
                    skipped++;
                    if (bundle.ConflictPolicy == TerminologyConflictPolicy.Prompt)
                        conflicted++;
                    continue;
                }

                if (row.Label == label && row.SourcePresetId == presetId)
                {
                    // Already at target state; no work to do.
                    skipped++;
                    continue;
                }

                row.Label = label;
                row.SourcePresetId = presetId;
                updated++;
            }
            else
            {
                // New row — preset seeded.
                db.TerminologyEntries.Add(new TerminologyEntry
                {
                    Key = key,
                    Label = label,
                    IsAdminEdited = false,
                    SourcePresetId = presetId,
                });
                added++;
            }
        }

        return new LayerApplyResult(
            Layer: PresetBundleLayer.Terminology,
            AddedCount: added,
            UpdatedCount: updated,
            SkippedCount: skipped,
            ConflictedCount: conflicted);
    }
}
