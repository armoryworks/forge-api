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
    DateTimeOffset CreatedAt);
