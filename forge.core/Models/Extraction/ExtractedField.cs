namespace Forge.Core.Models.Extraction;

/// <summary>
/// One extracted value plus how it was found.
///
/// <para>The evidence string is not decoration. A reviewer approving a draft
/// needs to see the text the number came from, because "quantity 500" is
/// unverifiable while "matched 'please ship 500 ea' in PO-8832.pdf" can be
/// checked in a second.</para>
/// </summary>
public sealed record ExtractedField<T>(
    T Value,
    ExtractionConfidence Confidence,
    /// <summary>The literal text the value was read from.</summary>
    string? Evidence = null,
    /// <summary>Which source it came from, so the UI can link to the right artifact.</summary>
    int? ArtifactId = null);

public enum ExtractionConfidence
{
    /// <summary>An unambiguous, labelled match ("PO Number: 8832"). Safe to pre-fill.</summary>
    High,

    /// <summary>A plausible but unlabelled match. Pre-filled and flagged for the reviewer to confirm.</summary>
    Medium,

    /// <summary>A guess. Never pre-filled — surfaced as a suggestion only.</summary>
    Low,
}
