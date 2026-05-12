using Forge.Api.Capabilities.Discovery.Bundles;

namespace Forge.Api.Capabilities.Discovery;

/// <summary>
/// Phase 4 Phase-F — Preset definition. Each preset names a known business
/// profile (Two-Person Shop, Growing Job Shop, etc.) and spells out which
/// capabilities should be enabled when the preset is applied.
///
/// Mirrors the Phase 4B design (preset-design.md). The preset catalog is
/// static; mutation lives at the capability level (preset apply is just a
/// bulk-toggle to the preset's target state).
///
/// <see cref="EnabledCapabilities"/> is the FULL set of capabilities the
/// preset wants enabled — both default-on entries (always on regardless)
/// and explicit additions. Capabilities NOT in this set are disabled when
/// the preset is applied. PRESET-CUSTOM has an empty list (no defaults
/// added; per 4B Open Question 5 / 4F-decisions-log, Custom inherits the
/// 41 catalog defaults from <see cref="CapabilityCatalog"/> at apply time).
///
/// <para><b>Pro Services rollout (Artifact 5):</b> presets also carry
/// optional per-layer seed bundles (terminology, reference data, track
/// types, roles, report visibility, folder maps, workflow definitions,
/// dashboards). Existing presets (01-07 + Custom) leave all bundles null;
/// apply-preset skips bundle steps when null. PRESET-08 / PRESET-09 fill
/// in the bundles. See <c>docs/pro-services-rollout/phase-1-analysis/
/// 05-preset-format-extension.md</c> for the full schema rationale.</para>
/// </summary>
public record PresetDefinition(
    string Id,
    string Name,
    string ShortDescription,
    string TargetProfile,
    IReadOnlyList<string> EnabledCapabilities,
    bool IsCustom = false,
    TerminologyBundle? TerminologyBundle = null,
    ReferenceDataBundle? ReferenceDataBundle = null,
    TrackTypeBundle? TrackTypeBundle = null,
    RoleBundle? RoleBundle = null,
    ReportVisibilityBundle? ReportVisibilityBundle = null,
    FolderMapBundle? FolderMapBundle = null,
    WorkflowDefinitionBundle? WorkflowDefinitionBundle = null,
    DashboardBundle? DashboardBundle = null);
