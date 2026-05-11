namespace QBEngineer.Api.Capabilities.Discovery.Bundles;

/// <summary>
/// Pro Services rollout (Artifact 5 §3.2) — per-preset reference-data
/// bundle. Apply-preset upserts the contained value seeds into the
/// <c>reference_data</c> table with <c>IsSeedData = true</c>, honoring
/// the conflict policy.
///
/// PRESET-08 seeds ~10 new groups (engagement_type, project_phase,
/// resource_skill, time_billable_status, time_activity_type,
/// deliverable_type, service_uom, engagement_status, retainer_status,
/// client_segment). PRESET-04 will carry the manufacturing groups
/// (re-applying a preset reasserts seed without clobbering admin edits).
/// </summary>
/// <param name="Groups">
/// Map of <c>group_code</c> → ordered list of values to seed for that
/// group. Apply pipeline writes one row per (group, code) pair.
/// </param>
/// <param name="ConflictPolicy">
/// Conflict policy for apply. Default = <see cref="ReferenceDataConflictPolicy.UpsertSeed"/>.
/// </param>
public sealed record ReferenceDataBundle(
    IReadOnlyDictionary<string, IReadOnlyList<ReferenceDataValueSeed>> Groups,
    ReferenceDataConflictPolicy ConflictPolicy = ReferenceDataConflictPolicy.UpsertSeed);

/// <summary>One reference-data value to seed.</summary>
/// <param name="Code">Stable code within the group, e.g. <c>"consulting"</c>.</param>
/// <param name="Label">Human display label.</param>
/// <param name="SortOrder">Display ordering within the group.</param>
/// <param name="Metadata">Optional JSON string for color, icon, etc.</param>
public sealed record ReferenceDataValueSeed(
    string Code,
    string Label,
    int SortOrder = 0,
    string? Metadata = null);

/// <summary>How apply-preset handles reference-data values that already exist.</summary>
public enum ReferenceDataConflictPolicy
{
    /// <summary>Add missing values, leave admin-customized values alone (default).</summary>
    UpsertSeed,
    /// <summary>Re-seed all values, overwriting any admin customization.</summary>
    Overwrite,
    /// <summary>Skip groups that already have any rows; only seed empty groups.</summary>
    Skip
}
