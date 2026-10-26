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
    decimal? TotalFunctional = null,
    int? PaymentTermsId = null,
    decimal DiscountPercent = 0m,
    DateTimeOffset? DiscountDueDate = null,
    DateTimeOffset? DiscountDeadline = null,
    decimal DiscountsTaken = 0m,
    decimal DiscountAvailable = 0m);
