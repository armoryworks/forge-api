namespace Forge.Core.Models;

public record CreatePaymentRequestModel(
    int CustomerId,
    string Method,
    decimal Amount,
    DateTimeOffset PaymentDate,
    string? ReferenceNumber,
    string? Notes,
    List<CreatePaymentApplicationModel>? Applications);

public record CreatePaymentApplicationModel(
    int InvoiceId,
    decimal Amount);
