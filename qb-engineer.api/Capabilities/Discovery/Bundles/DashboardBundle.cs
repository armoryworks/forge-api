namespace QBEngineer.Api.Capabilities.Discovery.Bundles;

/// <summary>
/// Pro Services rollout (Artifact 5 §3.8) — per-preset default dashboard
/// layout. Apply-preset seeds the contained role-keyed layouts so the
/// preset's primary roles land on a sensible default dashboard.
///
/// PRESET-08 seeds an "Engagement Manager" dashboard (Utilization KPI,
/// Billable %, AR Aging, Active Engagements list, Recent Deliverables,
/// Upcoming Milestones). Users can drag widgets around post-apply; the
/// seed is a starting point, not a constraint.
/// </summary>
/// <param name="LayoutsByRole">Map of role code → ordered widget seed list for that role.</param>
public sealed record DashboardBundle(
    IReadOnlyDictionary<string, IReadOnlyList<DashboardWidgetSeed>> LayoutsByRole);

/// <summary>One widget on a dashboard.</summary>
/// <param name="WidgetCode">Stable widget code (matches a registered widget on the frontend), e.g. <c>"billable_percent_kpi"</c>.</param>
/// <param name="X">Grid column origin.</param>
/// <param name="Y">Grid row origin.</param>
/// <param name="Width">Grid width.</param>
/// <param name="Height">Grid height.</param>
/// <param name="Config">Optional per-widget configuration (e.g. date range, drill-down target).</param>
public sealed record DashboardWidgetSeed(
    string WidgetCode,
    int X,
    int Y,
    int Width,
    int Height,
    IReadOnlyDictionary<string, string>? Config = null);
