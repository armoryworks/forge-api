namespace Forge.Core.Models;

public record InvoiceListItemModel(
    int Id,
    string InvoiceNumber,
    int CustomerId,
    string CustomerName,
    string Status,
    DateTimeOffset InvoiceDate,
    DateTimeOffset DueDate,
    decimal Total,
    decimal AmountPaid,
    decimal BalanceDue,
    DateTimeOffset CreatedAt,
    string? CurrencyCode = null,
    int? CurrencyId = null,
    decimal FxRate = 1m,
    decimal? TotalFunctional = null);
