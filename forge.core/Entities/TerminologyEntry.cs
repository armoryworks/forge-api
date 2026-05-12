namespace Forge.Core.Entities;

/// <summary>
/// One terminology override per install. The active map (key → label)
/// loaded by <c>TerminologyService.load()</c> at app init. Falls back to
/// a humanized version of the key when no override exists.
///
/// <para><b>Pro Services rollout (Artifact 4 §3.1):</b> rows now carry
/// <see cref="IsAdminEdited"/> and <see cref="SourcePresetId"/> so the
/// apply-preset pipeline can honor admin edits when re-applying or
/// switching presets (per <see cref="Capabilities.Discovery.Bundles.TerminologyBundle.ConflictPolicy"/>).</para>
/// </summary>
public class TerminologyEntry : BaseAuditableEntity
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// True if the admin has edited this key (directly via the Terminology
    /// admin UI, not via preset apply). When true and the conflict policy
    /// is <c>SkipAdminEdited</c>, the apply-preset pipeline leaves this
    /// row untouched even if a preset bundle would otherwise rewrite it.
    /// </summary>
    public bool IsAdminEdited { get; set; }

    /// <summary>
    /// Preset Id (e.g. <c>"PRESET-08"</c>) that most recently seeded this
    /// row, or null if the row originated from admin edit. Used for
    /// debugging "where did this label come from?" and for the per-layer
    /// activity-log row emitted by apply-preset.
    /// </summary>
    public string? SourcePresetId { get; set; }
}
