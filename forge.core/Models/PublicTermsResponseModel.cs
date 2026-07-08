namespace Forge.Core.Models;

/// <summary>
/// The immutable snapshot payload behind the anonymous "view full terms"
/// link — what was compiled at send time, never the live documents.
/// </summary>
public record PublicTermsResponseModel(
    string QuoteNumber,
    string CompiledHtml,
    DateTimeOffset SentAt);
