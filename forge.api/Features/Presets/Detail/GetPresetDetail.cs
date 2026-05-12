using MediatR;

using Forge.Api.Capabilities;
using Forge.Api.Capabilities.Discovery;
using Forge.Api.Features.Presets.List;
using Forge.Api.Features.Presets.Models;

namespace Forge.Api.Features.Presets.Detail;

/// <summary>
/// Phase 4 Phase-G — Returns the full descriptor for a single preset:
///   • Capability set (every catalog row tagged in/out of the preset).
///   • Delta vs catalog defaults (which capabilities the preset adds /
///     removes relative to a fresh-install baseline).
///   • Delta vs current install (which capabilities will change if the
///     preset is applied right now).
///
/// Custom is a special case: capability set = the 41 catalog defaults
/// (per 4B Open Question 5 / 4F Phase-F decision), no delta vs catalog.
/// </summary>
public record GetPresetDetailQuery(string PresetId)
    : IRequest<PresetDetailResponseModel?>;

public class GetPresetDetailHandler(ICapabilitySnapshotProvider snapshots)
    : IRequestHandler<GetPresetDetailQuery, PresetDetailResponseModel?>
{
    public Task<PresetDetailResponseModel?> Handle(
        GetPresetDetailQuery request,
        CancellationToken cancellationToken)
    {
        var preset = PresetCatalog.FindById(request.PresetId);
        if (preset is null) return Task.FromResult<PresetDetailResponseModel?>(null);

        // Resolve the preset's effective capability set. Custom inherits
        // catalog defaults at apply time.
        var effectiveSet = preset.IsCustom
            ? new HashSet<string>(
                CapabilityCatalog.All.Where(c => c.IsDefaultOn).Select(c => c.Code),
                StringComparer.Ordinal)
            : new HashSet<string>(preset.EnabledCapabilities, StringComparer.Ordinal);

        var defaultsSet = new HashSet<string>(
            CapabilityCatalog.All.Where(c => c.IsDefaultOn).Select(c => c.Code),
            StringComparer.Ordinal);

        // Capability rows: every catalog entry, tagged in/out of preset.
        var capabilities = CapabilityCatalog.All
            .OrderBy(c => c.Area, StringComparer.Ordinal)
            .ThenBy(c => c.Code, StringComparer.Ordinal)
            .Select(c => new PresetCapabilityRowResponseModel(
                Code: c.Code,
                Name: c.Name,
                Area: c.Area,
                Description: c.Description,
                InPreset: effectiveSet.Contains(c.Code),
                DefaultOn: c.IsDefaultOn))
            .ToList();

        // Delta vs catalog: capabilities where (in preset) != (default-on).
        var deltaVsCatalog = capabilities
            .Where(r => r.InPreset != r.DefaultOn)
            .ToList();

        // Delta vs current install: capabilities where (in preset) != (currently enabled).
        var snapshot = snapshots.Current;
        var deltaVsInstall = CapabilityCatalog.All
            .OrderBy(c => c.Area, StringComparer.Ordinal)
            .ThenBy(c => c.Code, StringComparer.Ordinal)
            .Select(c =>
            {
                var currentlyEnabled = snapshot.EnabledByCode.TryGetValue(c.Code, out var v) && v;
                var willBeEnabled = effectiveSet.Contains(c.Code);
                return (Capability: c, currentlyEnabled, willBeEnabled);
            })
            .Where(t => t.currentlyEnabled != t.willBeEnabled)
            .Select(t => new PresetCapabilityDeltaResponseModel(
                Code: t.Capability.Code,
                Name: t.Capability.Name,
                Area: t.Capability.Area,
                CurrentlyEnabled: t.currentlyEnabled,
                WillBeEnabled: t.willBeEnabled))
            .ToList();

        var capabilityCount = effectiveSet.Count;

        var detail = new PresetDetailResponseModel(
            Id: preset.Id,
            Name: preset.Name,
            ShortDescription: preset.ShortDescription,
            TargetProfile: preset.TargetProfile,
            CapabilityCount: capabilityCount,
            IsCustom: preset.IsCustom,
            IsActive: !preset.IsCustom && PresetStateComparer.MatchesCurrent(preset, snapshot.EnabledByCode),
            RecommendedFor: PresetMetadata.GetRecommendedForTags(preset.Id),
            Capabilities: capabilities,
            DeltaVsCatalogDefaults: deltaVsCatalog,
            DeltaVsCurrentInstall: deltaVsInstall);

        return Task.FromResult<PresetDetailResponseModel?>(detail);
    }
}
