namespace QBEngineer.Api.Capabilities.Discovery.Bundles;

/// <summary>
/// Pro Services rollout (Artifact 5 §3.5) — per-preset report-visibility
/// filter. Apply-preset writes the set of visible report codes into the
/// install's report-visibility settings; reports NOT in the set are
/// hidden from the reports list.
///
/// Null bundle (the default for PRESET-01 through PRESET-07) preserves
/// the current behavior — all 30 reports visible to every install.
/// PRESET-08 narrows to ~7-10 service-shop reports (Engagement P&amp;L,
/// Utilization, Billable %, AR Aging, etc.); PRESET-09 (Hybrid) keeps
/// the full set.
/// </summary>
/// <param name="VisibleReportCodes">
/// Report codes that should be visible under this preset. Absence
/// means hidden. Empty set = hide all (rare).
/// </param>
public sealed record ReportVisibilityBundle(
    IReadOnlySet<string> VisibleReportCodes);
