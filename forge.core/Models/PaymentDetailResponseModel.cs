namespace Forge.Core.Models;

public record PaymentDetailResponseModel(
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
    string? Notes,
    List<PaymentApplicationResponseModel> Applications,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    uint RowVersion = 0,
    bool CanVoid = false,
    int? CurrencyId = null,
    string? CurrencyCode = null,
    decimal FxRate = 1m);
