using Forge.Core.Models.Extraction;

namespace Forge.Core.Interfaces.Communications;

/// <summary>
/// Reads text and proposes an order. One method, no side effects.
///
/// <para>Deliberately the narrowest possible contract so the implementation can
/// change without the pipeline noticing. A regex pass, a local LLM, a vendor
/// document-AI service and a per-customer template parser all fit behind it. The
/// ingestion path depends on this interface and never on a concrete extractor.</para>
///
/// <para><b>An extractor never throws for bad input.</b> Unreadable text returns
/// <see cref="ExtractionResult.Empty"/> with a warning. The draft is created
/// either way, because a human is going to review it regardless and a blank form
/// beside the original message is more useful than a failed job.</para>
/// </summary>
public interface IOrderExtractor
{
    /// <summary>Stable id recorded on the result, so a later implementation swap stays traceable in the audit trail.</summary>
    string ExtractorId { get; }

    Task<ExtractionResult> ExtractAsync(ExtractionRequest request, CancellationToken ct);
}
