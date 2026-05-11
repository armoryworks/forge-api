namespace QBEngineer.Api.Capabilities.Discovery.Bundles;

/// <summary>
/// Pro Services rollout (Artifact 5 §3.1) — per-preset terminology bundle.
/// Apply-preset writes the contained <see cref="Labels"/> map into the
/// <c>terminology_overrides</c> table, honoring the conflict policy for
/// keys the admin has previously edited.
///
/// PRESET-08 (Pro Services) carries ~30-40 renames; PRESET-09 (Hybrid)
/// carries a partial overlay (~10-15 renames covering shared service
/// vocabulary). Existing presets (01-07) carry null bundles — apply
/// pipeline skips the terminology step for them.
/// </summary>
/// <param name="Labels">
/// Key → label mapping. Keys match the terminology key convention
/// (<c>entity_*</c>, <c>status_*</c>, <c>action_*</c>, <c>label_*</c>).
/// </param>
/// <param name="LocaleOverlays">
/// Optional locale-specific overlays. The default <see cref="Labels"/>
/// map is treated as en-US unless a locale entry overrides.
/// </param>
/// <param name="ConflictPolicy">
/// Conflict policy for apply. Default = <see cref="TerminologyConflictPolicy.SkipAdminEdited"/>.
/// </param>
public sealed record TerminologyBundle(
    IReadOnlyDictionary<string, string> Labels,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? LocaleOverlays = null,
    TerminologyConflictPolicy ConflictPolicy = TerminologyConflictPolicy.SkipAdminEdited);

/// <summary>
/// How apply-preset handles terminology keys that the admin has already
/// edited (tracked via <c>terminology_overrides.is_admin_edited</c>).
/// </summary>
public enum TerminologyConflictPolicy
{
    /// <summary>Don't touch keys the admin has edited (default).</summary>
    SkipAdminEdited,
    /// <summary>Re-seed all keys regardless of admin edits (use for first-apply or migration).</summary>
    Overwrite,
    /// <summary>Apply pipeline returns conflict list; UI presents a merge dialog.</summary>
    Prompt
}
