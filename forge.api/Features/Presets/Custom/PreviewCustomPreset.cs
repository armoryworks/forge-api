using FluentValidation;
using MediatR;

using Forge.Api.Capabilities;
using Forge.Api.Features.Presets.Models;

namespace Forge.Api.Features.Presets.Custom;

/// <summary>
/// Phase 4 Phase-G — Stateless preview of a Custom preset construction.
/// Caller supplies a list of capability overrides (per-capability
/// enabled/disabled toggles). The handler:
///   1. Starts from the catalog defaults baseline (the 41 default-on rows
///      per 4B Open Question 5 / 4F Phase-F decision).
///   2. Applies each override.
///   3. Returns the resulting capability set + validation results
///      (missing dependencies, mutex conflicts, dependents-in-conflict).
///
/// Stateless — the per-install snapshot is consulted only to compute the
/// "delta vs current install" hint. No persistence.
/// </summary>
public record PreviewCustomPresetCommand(IReadOnlyList<PresetCustomOverrideRequestItem> CapabilityOverrides)
    : IRequest<PresetCustomPreviewResponseModel>;

public class PreviewCustomPresetValidator : AbstractValidator<PreviewCustomPresetCommand>
{
    public PreviewCustomPresetValidator()
    {
        RuleFor(x => x.CapabilityOverrides)
            .NotNull()
            .Must(items => items.Select(i => i.Code).Distinct(StringComparer.Ordinal).Count() == items.Count)
            .WithMessage("Duplicate capability IDs in override list.");
        RuleForEach(x => x.CapabilityOverrides).ChildRules(item =>
        {
            item.RuleFor(i => i.Code).NotEmpty().Matches("^CAP-[A-Z0-9-]+$");
        });
    }
}

public class PreviewCustomPresetHandler(ICapabilitySnapshotProvider snapshots)
    : IRequestHandler<PreviewCustomPresetCommand, PresetCustomPreviewResponseModel>
{
    public Task<PresetCustomPreviewResponseModel> Handle(
        PreviewCustomPresetCommand request,
        CancellationToken cancellationToken)
    {
        // 1. Catalog defaults baseline.
        var targetSet = new HashSet<string>(
            CapabilityCatalog.All.Where(c => c.IsDefaultOn).Select(c => c.Code),
            StringComparer.Ordinal);

        // 2. Apply overrides.
        foreach (var ov in request.CapabilityOverrides)
        {
            if (ov.Enabled) targetSet.Add(ov.Code);
            else targetSet.Remove(ov.Code);
        }

        // 3. Build the capability rows (every catalog entry, tagged in/out
        //    of the resolved set).
        var capabilities = CapabilityCatalog.All
            .OrderBy(c => c.Area, StringComparer.Ordinal)
            .ThenBy(c => c.Code, StringComparer.Ordinal)
            .Select(c => new PresetCapabilityRowResponseModel(
                Code: c.Code,
                Name: c.Name,
                Area: c.Area,
                Description: c.Description,
                InPreset: targetSet.Contains(c.Code),
                DefaultOn: c.IsDefaultOn))
            .ToList();

        // 4. Delta vs current install state.
        var snapshot = snapshots.Current;
        var current = snapshot.EnabledByCode;
        var delta = new List<PresetCapabilityDeltaResponseModel>();
        foreach (var def in CapabilityCatalog.All)
        {
            var currentlyEnabled = current.TryGetValue(def.Code, out var v) && v;
            var willBeEnabled = targetSet.Contains(def.Code);
            if (currentlyEnabled != willBeEnabled)
            {
                delta.Add(new PresetCapabilityDeltaResponseModel(
                    Code: def.Code,
                    Name: def.Name,
                    Area: def.Area,
                    CurrentlyEnabled: currentlyEnabled,
                    WillBeEnabled: willBeEnabled));
            }
        }
        delta = [.. delta.OrderBy(d => d.Code, StringComparer.Ordinal)];

        // 5. Validate constraints on the candidate state (overlay current
        //    state with target).
        var candidate = new Dictionary<string, bool>(current, StringComparer.Ordinal);
        foreach (var d in delta) candidate[d.Code] = d.WillBeEnabled;

        var violations = new List<PresetApplyViolationResponseModel>();
        foreach (var d in delta)
        {
            if (d.WillBeEnabled)
            {
                var missing = CapabilityDependencyResolver.FindMissingDependencies(d.Code, candidate);
                if (missing.Count > 0)
                {
                    violations.Add(new PresetApplyViolationResponseModel(
                        Code: "capability-missing-dependencies",
                        Capability: d.Code,
                        Message: $"'{d.Code}' requires: {string.Join(", ", missing)}",
                        Missing: missing,
                        Conflicts: null,
                        Dependents: null));
                }
                var conflicts = CapabilityDependencyResolver.FindEnabledMutexConflicts(d.Code, candidate);
                if (conflicts.Count > 0)
                {
                    violations.Add(new PresetApplyViolationResponseModel(
                        Code: "capability-mutex-violation",
                        Capability: d.Code,
                        Message: $"'{d.Code}' conflicts with enabled: {string.Join(", ", conflicts)}",
                        Missing: null,
                        Conflicts: conflicts,
                        Dependents: null));
                }
            }
            else
            {
                var dependents = CapabilityDependencyResolver.FindEnabledDependents(d.Code, candidate);
                if (dependents.Count > 0)
                {
                    violations.Add(new PresetApplyViolationResponseModel(
                        Code: "capability-has-dependents",
                        Capability: d.Code,
                        Message: $"'{d.Code}' is required by: {string.Join(", ", dependents)}",
                        Missing: null,
                        Conflicts: null,
                        Dependents: dependents));
                }
            }
        }

        return Task.FromResult(new PresetCustomPreviewResponseModel(
            CapabilityCount: targetSet.Count,
            Capabilities: capabilities,
            DeltaVsCurrentInstall: delta,
            Valid: violations.Count == 0,
            Violations: violations));
    }
}
