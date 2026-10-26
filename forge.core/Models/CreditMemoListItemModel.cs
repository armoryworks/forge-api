namespace Forge.Core.Models;

public record CreditMemoListItemModel(
    int Id,
    string CreditMemoNumber,
    int CustomerId,
    string CustomerName,
    int? InvoiceId,
    string? InvoiceNumber,
    string ReasonType,
    string Reason,
    string Status,
    DateTimeOffset CreditDate,
    decimal Total,
    decimal OpenAmount,
    int CurrencyId,
    string? CurrencyCode,
    decimal FxRate,
    DateTimeOffset CreatedAt);
