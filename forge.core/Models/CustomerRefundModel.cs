namespace Forge.Core.Models;

public record CustomerRefundModel(
    int Id,
    string RefundNumber,
    int CustomerId,
    decimal Amount,
    string Method,
    DateTimeOffset RefundDate,
    string? Reference,
    string Status,
    int? CreditMemoId,
    string? CreditMemoNumber,
    int? PaymentId,
    string? PaymentNumber,
    string? CurrencyCode,
    decimal FxRate);
