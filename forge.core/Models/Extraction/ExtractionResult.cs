namespace Forge.Core.Models.Extraction;

/// <summary>
/// What an extractor found. Every field is optional and an empty result is a
/// legitimate, successful outcome.
///
/// <para>Extraction failure degrades to a blank draft beside the source message.
/// It never guesses and never blocks: an unparseable PO still produces a
/// reviewable draft with the original attached, which is strictly better than an
/// error the operator has to go hunting for.</para>
/// </summary>
public sealed record ExtractionResult(
    IReadOnlyList<ExtractedOrderLine> Lines,
    ExtractedField<string>? CustomerPoNumber = null,
    ExtractedField<DateTimeOffset>? NeedByDate = null,
    /// <summary>Things the reviewer should know — ambiguity, conflicting values, text that could not be read.</summary>
    IReadOnlyList<string>? Warnings = null,
    /// <summary>Extractor that produced this, recorded so a later change of implementation is traceable.</summary>
    string? ExtractorId = null)
{
    /// <summary>The honest empty answer. Not an error — the draft is still created, just blank.</summary>
    public static ExtractionResult Empty(string extractorId, params string[] warnings) =>
        new([], null, null, warnings.Length == 0 ? null : warnings, extractorId);

    public bool FoundAnything =>
        Lines.Count > 0 || CustomerPoNumber is not null || NeedByDate is not null;
}
