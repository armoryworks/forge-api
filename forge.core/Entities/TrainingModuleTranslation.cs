namespace Forge.Core.Entities;

/// <summary>
/// A per-locale translation of a <see cref="TrainingModule"/>'s human-readable content
/// (title, summary, and the full content JSON — body / walkthrough step text / quiz /
/// quick-reference). The base module row holds the canonical English; the API overlays a
/// translation when one exists for the requested locale, falling back to English otherwise.
/// </summary>
public class TrainingModuleTranslation : BaseAuditableEntity
{
    public int TrainingModuleId { get; set; }

    /// <summary>BCP-47-ish locale code, e.g. "es". "en" is the canonical base and is not stored here.</summary>
    public string Locale { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string ContentJson { get; set; } = "{}";

    public TrainingModule? TrainingModule { get; set; }
}
