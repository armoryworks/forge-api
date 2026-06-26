namespace Forge.Core.Entities;

/// <summary>
/// A per-locale translation of a <see cref="TrainingPath"/>'s title + description. The base
/// path row holds the canonical English; the API overlays a translation when one exists for
/// the requested locale, falling back to English otherwise.
/// </summary>
public class TrainingPathTranslation : BaseAuditableEntity
{
    public int TrainingPathId { get; set; }

    /// <summary>BCP-47-ish locale code, e.g. "es". "en" is the canonical base and is not stored here.</summary>
    public string Locale { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public TrainingPath? TrainingPath { get; set; }
}
