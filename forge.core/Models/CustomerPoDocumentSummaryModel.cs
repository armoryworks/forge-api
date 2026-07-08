namespace Forge.Core.Models;

/// <summary>
/// Thin identity view of a <c>CustomerPoDocument</c> row — returned by the
/// generate command. The full document body renders LIVE from the sales
/// order via <c>CustomerPoDocumentResponseModel</c>.
/// </summary>
public record CustomerPoDocumentSummaryModel(
    int Id,
    int SalesOrderId,
    string DocumentNumber,
    int? GeneratedFromQuoteId,
    DateTimeOffset GeneratedAt);
