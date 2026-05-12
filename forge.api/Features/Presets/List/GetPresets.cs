using MediatR;

using Forge.Api.Capabilities;
using Forge.Api.Capabilities.Discovery;
using Forge.Api.Features.Presets.Models;

namespace Forge.Api.Features.Presets.List;

/// <summary>
/// Phase 4 Phase-G — Returns a summary descriptor for every known preset.
/// Used by the preset browser screen to render the card grid.
///
/// "IsActive" is best-effort: a preset is marked active when the current
/// install state EXACTLY matches the preset's enabled-capability set. This
/// is a non-binding hint for the UI ("Active preset: PRESET-04"); install
/// state can drift via per-capability toggles after a preset is applied,
/// in which case no preset shows as active.
/// </summary>
public record GetPresetsQuery() : IRequest<IReadOnlyList<PresetSummaryResponseModel>>;

public class GetPresetsHandler(ICapabilitySnapshotProvider snapshots)
    : IRequestHandler<GetPresetsQuery, IReadOnlyList<PresetSummaryResponseModel>>
{
    public Task<IReadOnlyList<PresetSummaryResponseModel>> Handle(
        GetPresetsQuery request,
        CancellationToken cancellationToken)
    {
        var snapshot = snapshots.Current;
        var summaries = PresetCatalog.All
            .Select(preset =>
            {
                var capabilityCount = preset.IsCustom
                    ? CapabilityCatalog.All.Count(c => c.IsDefaultOn)
                    : preset.EnabledCapabilities.Count;
                return new PresetSummaryResponseModel(
                    Id: preset.Id,
                    Name: preset.Name,
                    ShortDescription: preset.ShortDescription,
                    TargetProfile: preset.TargetProfile,
                    CapabilityCount: capabilityCount,
                    IsCustom: preset.IsCustom,
                    IsActive: !preset.IsCustom && PresetStateComparer.MatchesCurrent(preset, snapshot.EnabledByCode),
                    RecommendedFor: PresetMetadata.GetRecommendedForTags(preset.Id));
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<PresetSummaryResponseModel>>(summaries);
    }
}

/// <summary>
/// Phase 4 Phase-G — Best-effort match between an install's current capability
/// state and a preset's enabled set. A preset matches if every catalog entry
/// is in agreement: capabilities the preset enables are enabled, and
/// capabilities the preset does NOT include are disabled.
/// </summary>
internal static class PresetStateComparer
{
    public static bool MatchesCurrent(
        PresetDefinition preset,
        IReadOnlyDictionary<string, bool> currentState)
    {
        var presetSet = new HashSet<string>(preset.EnabledCapabilities, StringComparer.Ordinal);
        foreach (var def in CapabilityCatalog.All)
        {
            var currentlyEnabled = currentState.TryGetValue(def.Code, out var v) && v;
            var shouldBeEnabled = presetSet.Contains(def.Code);
            if (currentlyEnabled != shouldBeEnabled) return false;
        }
        return true;
    }
}
