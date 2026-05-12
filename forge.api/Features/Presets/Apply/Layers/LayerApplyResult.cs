namespace Forge.Api.Features.Presets.Apply.Layers;

/// <summary>
/// Pro Services rollout (Artifact 5 §4) — outcome of applying one preset
/// bundle (terminology, reference data, track types, etc.). Returned by
/// each <c>*BundleApplier.Apply</c> static method and aggregated by
/// <c>ApplyPresetHandler</c> into the response + audit row.
/// </summary>
/// <param name="Layer">Layer name (matches <see cref="PresetBundleLayer"/> as a string).</param>
/// <param name="AddedCount">Rows newly inserted by this apply.</param>
/// <param name="UpdatedCount">Existing rows the bundle re-asserted (label, sort-order, etc.).</param>
/// <param name="SkippedCount">Rows the bundle would have written but conflict policy held off (e.g. admin-edited terminology).</param>
/// <param name="ConflictedCount">Rows where the bundle's intent disagrees with current state under a Prompt policy — caller must resolve.</param>
public sealed record LayerApplyResult(
    string Layer,
    int AddedCount,
    int UpdatedCount,
    int SkippedCount,
    int ConflictedCount = 0)
{
    /// <summary>Convenience: total rows touched (added + updated).</summary>
    public int TouchedCount => AddedCount + UpdatedCount;

    /// <summary>Empty result for null bundles.</summary>
    public static LayerApplyResult Empty(string layer) => new(layer, 0, 0, 0, 0);
}

/// <summary>Layer-name constants. String form matches the audit-row JSON.</summary>
public static class PresetBundleLayer
{
    public const string Terminology = "terminology";
    public const string ReferenceData = "referenceData";
    public const string TrackType = "trackType";
    public const string Role = "role";
    public const string ReportVisibility = "reportVisibility";
    public const string FolderMap = "folderMap";
    public const string WorkflowDefinition = "workflowDefinition";
    public const string Dashboard = "dashboard";
}
