using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities.Discovery.Bundles;
using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Api.Features.Presets.Apply.Layers;

/// <summary>
/// Pro Services rollout (Artifact 5 §3.3, §4) — applies a
/// <see cref="TrackTypeBundle"/>: each track type is upserted by code,
/// and its stages are upserted (also by code) within that track type.
///
/// <para><b>Conflict semantics:</b></para>
/// <list type="bullet">
///   <item><c>UpsertByCode</c> (default): add missing track types; for
///   existing ones, leave their core fields untouched but reconcile
///   stages — add missing stages, leave existing ones alone.</item>
///   <item><c>AddOnly</c>: only add net-new track types; never modify
///   anything (including existing track types' stage lists).</item>
///   <item><c>Replace</c>: dangerous — reserved for first-time apply.
///   Replaces all track types + stages. Not used by default.</item>
/// </list>
///
/// <para>The applier never deletes track types or stages — soft-delete is
/// up to admin via the Track Types page. The catalog presents what the
/// preset *wants*, not a mandate.</para>
///
/// <para>Caller is responsible for <c>SaveChangesAsync</c>.</para>
/// </summary>
public static class TrackTypeBundleApplier
{
    public static async Task<LayerApplyResult> ApplyAsync(
        TrackTypeBundle bundle,
        AppDbContext db,
        string presetId,
        CancellationToken cancellationToken)
    {
        _ = presetId;  // Reserved for future linkage; TrackType has no source_preset_id today.

        // Replace policy is structurally distinct — bail to a separate
        // implementation. (Today: not invoked by any preset; left as a
        // hard NotImplemented so a future caller has to think it through.)
        if (bundle.ConflictPolicy == TrackTypeConflictPolicy.Replace)
        {
            throw new NotImplementedException(
                "TrackTypeConflictPolicy.Replace is not implemented. Reserved for first-time-install apply.");
        }

        var seedCodes = bundle.TrackTypes.Select(t => t.Code).ToList();
        var existingTracks = await db.TrackTypes
            .Where(t => seedCodes.Contains(t.Code))
            .Include(t => t.Stages)
            .ToListAsync(cancellationToken);

        var trackByCode = existingTracks.ToDictionary(t => t.Code, t => t, StringComparer.Ordinal);

        var added = 0;
        var updated = 0;
        var skipped = 0;

        foreach (var seed in bundle.TrackTypes)
        {
            if (trackByCode.TryGetValue(seed.Code, out var track))
            {
                // Existing track type — never modify the track type itself.
                // For UpsertByCode, reconcile stages (add missing only).
                // For AddOnly, skip entirely.
                if (bundle.ConflictPolicy == TrackTypeConflictPolicy.AddOnly)
                {
                    skipped++;
                    continue;
                }

                var stagesByCode = track.Stages.ToDictionary(s => s.Code, s => s, StringComparer.Ordinal);
                var stagesAdded = 0;
                foreach (var stageSeed in seed.Stages)
                {
                    if (stagesByCode.ContainsKey(stageSeed.Code))
                    {
                        skipped++;
                        continue;
                    }
                    db.JobStages.Add(BuildStage(track.Id, stageSeed));
                    stagesAdded++;
                }
                if (stagesAdded > 0) updated++;
                else skipped++;
            }
            else
            {
                // New track type — insert with stages in one go via nav.
                var newTrack = new TrackType
                {
                    Code = seed.Code,
                    Name = seed.Name,
                    SortOrder = seed.SortOrder,
                    IsDefault = seed.IsDefault,
                    IsShopFloor = seed.IsShopFloor,
                    IsActive = true,
                };
                foreach (var stageSeed in seed.Stages)
                {
                    newTrack.Stages.Add(BuildStage(trackTypeId: 0, stageSeed));
                }
                db.TrackTypes.Add(newTrack);
                added++;
            }
        }

        return new LayerApplyResult(
            Layer: PresetBundleLayer.TrackType,
            AddedCount: added,
            UpdatedCount: updated,
            SkippedCount: skipped);
    }

    private static JobStage BuildStage(int trackTypeId, JobStageSeed s) => new()
    {
        TrackTypeId = trackTypeId,
        Code = s.Code,
        Name = s.Name,
        SortOrder = s.SortOrder,
        Color = s.Color,
        IsShopFloor = s.IsShopFloor,
        IsIrreversible = s.IsIrreversible,
        AccountingDocumentType = s.AccountingDocumentType,
        WIPLimit = s.WipLimit,
        IsActive = true,
    };
}
