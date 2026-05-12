using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities.Discovery.Bundles;
using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Api.Features.Presets.Apply.Layers;

/// <summary>
/// Pro Services rollout (Artifact 5 §3.6, §4) — applies a
/// <see cref="FolderMapBundle"/> by persisting the suggestion catalog
/// as a single <c>SystemSetting</c> row keyed by
/// <c>cloud_storage.folder_map</c>. The dual-path auto-create flow (per
/// D2) consults this catalog when an entity is created with
/// <c>CAP-EXT-CLOUD-STORAGE</c> enabled, deciding whether to anchor a
/// folder for that entity type and how to name it.
///
/// <para>The bundle is a <em>declaration</em>, not eager folder creation:
/// no provider-side API calls happen during apply. The actual folder
/// creation is lazy, deferred to when the triggering entity is created.</para>
///
/// <para>Re-apply semantics: the entire bundle is serialized to JSON
/// and upserted into the single setting row. Re-applying replaces the
/// suggestion list wholesale — admins who hand-edit folder maps via the
/// admin UI should be aware preset re-apply will overwrite. (A future
/// enhancement could surface conflict via a Prompt policy similar to
/// terminology, but the bundle today is small enough that a wholesale
/// replace is reasonable.)</para>
/// </summary>
public static class FolderMapBundleApplier
{
    public const string FolderMapSettingKey = "cloud_storage.folder_map";

    public static async Task<LayerApplyResult> ApplyAsync(
        FolderMapBundle bundle,
        AppDbContext db,
        string presetId,
        CancellationToken cancellationToken)
    {
        _ = presetId;  // Reserved for future per-preset routing; today we store one global blob.

        // Serialize the bundle's suggestion list (NOT the whole bundle —
        // we don't need the wrapper type's metadata at read-time).
        var json = JsonSerializer.Serialize(bundle.Suggestions);

        var existing = await db.SystemSettings
            .FirstOrDefaultAsync(s => s.Key == FolderMapSettingKey, cancellationToken);

        if (existing is null)
        {
            db.SystemSettings.Add(new SystemSetting
            {
                Key = FolderMapSettingKey,
                Value = json,
                Description = $"Folder-map suggestion catalog (seeded by {presetId}).",
            });
            return new LayerApplyResult(
                Layer: PresetBundleLayer.FolderMap,
                AddedCount: bundle.Suggestions.Count,
                UpdatedCount: 0,
                SkippedCount: 0);
        }

        if (existing.Value == json)
        {
            // No change — bundle matches the current setting verbatim.
            return new LayerApplyResult(
                Layer: PresetBundleLayer.FolderMap,
                AddedCount: 0,
                UpdatedCount: 0,
                SkippedCount: bundle.Suggestions.Count);
        }

        existing.Value = json;
        existing.Description = $"Folder-map suggestion catalog (seeded by {presetId}).";
        return new LayerApplyResult(
            Layer: PresetBundleLayer.FolderMap,
            AddedCount: 0,
            UpdatedCount: bundle.Suggestions.Count,
            SkippedCount: 0);
    }
}
