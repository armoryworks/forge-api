namespace Forge.Core.Models;

/// <summary>
/// A vendor's open items: approved bills with a balance (positive) and issued credit memos with an open amount
/// (negative), oldest first, with the net balance per currency.
/// </summary>
public record VendorStatementModel(
    int VendorId,
    string VendorName,
    DateTimeOffset AsOf,
    List<VendorStatementLineModel> Lines,
    List<VendorStatementTotalModel> Totals);

public record VendorStatementLineModel(
    string DocumentType,
    int DocumentId,
    string DocumentNumber,
    string? VendorReference,
    DateTimeOffset DocumentDate,
    DateTimeOffset? DueDate,
    int CurrencyId,
    string? CurrencyCode,
    decimal OriginalAmount,
    decimal OpenAmount);

public record VendorStatementTotalModel(int CurrencyId, string? CurrencyCode, decimal OpenBalance);
