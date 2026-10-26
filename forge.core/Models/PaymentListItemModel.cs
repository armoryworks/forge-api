namespace Forge.Core.Models;

public record PaymentListItemModel(
    int Id,
    string PaymentNumber,
    int CustomerId,
    string CustomerName,
    string Method,
    decimal Amount,
    decimal AppliedAmount,
    decimal UnappliedAmount,
    DateTimeOffset PaymentDate,
    string? ReferenceNumber,
    DateTimeOffset CreatedAt,
    int? CurrencyId = null,
    string? CurrencyCode = null,
    decimal FxRate = 1m,
    decimal? TotalFunctional = null);
