namespace Forge.Core.Models.Extraction;

/// <summary>
/// Everything an extractor is given. Deliberately narrow: text, the party it
/// came from, and nothing else.
///
/// <para>No database handle, no services. An extractor reads and proposes; it
/// never writes, never resolves master data, and never decides whether an order
/// should exist. That is what makes the implementation swappable — a regex
/// version, an LLM version and a vendor-API version all satisfy this shape
/// without the pipeline changing.</para>
/// </summary>
public sealed record ExtractionRequest(
    IReadOnlyList<ExtractionSource> Sources,

    /// <summary>The resolved customer, when one is known. Lets an extractor bias toward that customer's part numbering.</summary>
    int? CustomerId = null,

    /// <summary>Subject line, when there is one. Purchase-order numbers live there surprisingly often.</summary>
    string? Subject = null);
