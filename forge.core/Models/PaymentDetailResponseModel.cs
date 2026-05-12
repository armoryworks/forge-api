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
    DateTimeOffset UpdatedAt);
