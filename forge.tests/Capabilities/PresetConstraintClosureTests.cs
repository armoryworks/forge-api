using FluentAssertions;

using Forge.Api.Capabilities;
using Forge.Api.Capabilities.Discovery;

namespace Forge.Tests.Capabilities;

/// <summary>
/// Every preset's capability set must be applyable as-is: closed under the
/// dependency edges and free of mutex conflicts declared in
/// <see cref="CapabilityCatalogRelations"/>. Preset apply is a full-state
/// sync (see PreviewPresetApplyHandler) — anything outside the set gets
/// disabled — so a preset that references a capability without carrying its
/// dependencies produces an apply that is permanently blocked by the
/// constraint validator. A fresh install on PRESET-06 hit exactly this
/// (CAP-PLAN-MPS without CAP-PLAN-FORECAST, 2026-07-20).
/// </summary>
public class PresetConstraintClosureTests
{
    public static TheoryData<string> PresetIds()
    {
        var data = new TheoryData<string>();
        foreach (var preset in PresetCatalog.All)
            data.Add(preset.Id);
        return data;
    }

    private static IReadOnlySet<string> EffectiveSet(PresetDefinition preset) =>
        preset.IsCustom
            ? new HashSet<string>(
                CapabilityCatalog.All.Where(c => c.IsDefaultOn).Select(c => c.Code),
                StringComparer.Ordinal)
            : new HashSet<string>(preset.EnabledCapabilities, StringComparer.Ordinal);

    [Theory]
    [MemberData(nameof(PresetIds))]
    public void Preset_capabilities_all_exist_in_catalog(string presetId)
    {
        var preset = PresetCatalog.FindById(presetId)!;
        var known = new HashSet<string>(
            CapabilityCatalog.All.Select(c => c.Code), StringComparer.Ordinal);

        EffectiveSet(preset).Should().BeSubsetOf(
            known, because: $"{preset.Id} must only reference cataloged capabilities");
    }

    [Theory]
    [MemberData(nameof(PresetIds))]
    public void Preset_is_dependency_closed_and_mutex_free(string presetId)
    {
        var preset = PresetCatalog.FindById(presetId)!;
        var set = EffectiveSet(preset);
        var enabled = CapabilityCatalog.All.ToDictionary(
            c => c.Code, c => set.Contains(c.Code), StringComparer.Ordinal);

        var problems = new List<string>();
        foreach (var code in set)
        {
            var missing = CapabilityDependencyResolver.FindMissingDependencies(code, enabled);
            if (missing.Count > 0)
                problems.Add($"{code} requires missing: {string.Join(", ", missing)}");

            var conflicts = CapabilityDependencyResolver.FindEnabledMutexConflicts(code, enabled);
            if (conflicts.Count > 0)
                problems.Add($"{code} conflicts with: {string.Join(", ", conflicts)}");
        }

        problems.Should().BeEmpty(
            because: $"{preset.Id} ({preset.Name}) must be applyable without constraint violations");
    }
}
