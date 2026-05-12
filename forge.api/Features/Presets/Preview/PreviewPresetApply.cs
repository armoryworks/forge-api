using MediatR;

using Forge.Api.Capabilities;
using Forge.Api.Capabilities.Discovery;
using Forge.Api.Features.Presets.Models;

namespace Forge.Api.Features.Presets.Preview;

/// <summary>
/// Phase 4 Phase-G — Stateless preview of a preset apply. Returns the deltas
/// (which capabilities will change) and any constraint violations the apply
/// would hit (dependency missing, mutex conflict, dependents present). No
/// persistence; the UI calls this before showing the apply confirmation
/// modal.
///
/// Reuses the same dependency / mutex evaluation logic as the bulk-toggle
/// validate endpoint (Phase E) — we walk the same candidate state and call
/// <see cref="CapabilityDependencyResolver"/> for each changing row.
/// </summary>
public record PreviewPresetApplyQuery(string PresetId)
    : IRequest<PresetApplyPreviewResponseModel?>;

public class PreviewPresetApplyHandler(ICapabilitySnapshotProvider snapshots)
    : IRequestHandler<PreviewPresetApplyQuery, PresetApplyPreviewResponseModel?>
{
    public Task<PresetApplyPreviewResponseModel?> Handle(
        PreviewPresetApplyQuery request,
        CancellationToken cancellationToken)
    {
        var preset = PresetCatalog.FindById(request.PresetId);
        if (preset is null)
            return Task.FromResult<PresetApplyPreviewResponseModel?>(null);

        var snapshot = snapshots.Current;
        var current = snapshot.EnabledByCode;

        // Resolve the preset's effective target set (Custom = catalog defaults).
        var targetSet = preset.IsCustom
            ? new HashSet<string>(
                CapabilityCatalog.All.Where(c => c.IsDefaultOn).Select(c => c.Code),
                StringComparer.Ordinal)
            : new HashSet<string>(preset.EnabledCapabilities, StringComparer.Ordinal);

        // Compute deltas (changing rows only).
        var deltas = new List<PresetCapabilityDeltaResponseModel>();
        foreach (var def in CapabilityCatalog.All)
        {
            var currentlyEnabled = current.TryGetValue(def.Code, out var v) && v;
            var willBeEnabled = targetSet.Contains(def.Code);
            if (currentlyEnabled != willBeEnabled)
            {
                deltas.Add(new PresetCapabilityDeltaResponseModel(
                    Code: def.Code,
                    Name: def.Name,
                    Area: def.Area,
                    CurrentlyEnabled: currentlyEnabled,
                    WillBeEnabled: willBeEnabled));
            }
        }
        deltas = [.. deltas.OrderBy(d => d.Code, StringComparer.Ordinal)];

        // Build the candidate state map (current overlaid with target) and
        // walk the changing rows checking dependency / mutex constraints.
        var candidate = new Dictionary<string, bool>(current, StringComparer.Ordinal);
        foreach (var delta in deltas)
        {
            candidate[delta.Code] = delta.WillBeEnabled;
        }

        var violations = new List<PresetApplyViolationResponseModel>();
        foreach (var delta in deltas)
        {
            if (delta.WillBeEnabled)
            {
                var missing = CapabilityDependencyResolver.FindMissingDependencies(delta.Code, candidate);
                if (missing.Count > 0)
                {
                    violations.Add(new PresetApplyViolationResponseModel(
                        Code: "capability-missing-dependencies",
                        Capability: delta.Code,
                        Message: $"'{delta.Code}' requires: {string.Join(", ", missing)}",
                        Missing: missing,
                        Conflicts: null,
                        Dependents: null));
                }
                var conflicts = CapabilityDependencyResolver.FindEnabledMutexConflicts(delta.Code, candidate);
                if (conflicts.Count > 0)
                {
                    violations.Add(new PresetApplyViolationResponseModel(
                        Code: "capability-mutex-violation",
                        Capability: delta.Code,
                        Message: $"'{delta.Code}' conflicts with enabled: {string.Join(", ", conflicts)}",
                        Missing: null,
                        Conflicts: conflicts,
                        Dependents: null));
                }
            }
            else
            {
                var dependents = CapabilityDependencyResolver.FindEnabledDependents(delta.Code, candidate);
                if (dependents.Count > 0)
                {
                    violations.Add(new PresetApplyViolationResponseModel(
                        Code: "capability-has-dependents",
                        Capability: delta.Code,
                        Message: $"'{delta.Code}' is required by: {string.Join(", ", dependents)}",
                        Missing: null,
                        Conflicts: null,
                        Dependents: dependents));
                }
            }
        }

        return Task.FromResult<PresetApplyPreviewResponseModel?>(new PresetApplyPreviewResponseModel(
            PresetId: preset.Id,
            PresetName: preset.Name,
            IsCustom: preset.IsCustom,
            DeltaCount: deltas.Count,
            Deltas: deltas,
            Valid: violations.Count == 0,
            Violations: violations));
    }
}
