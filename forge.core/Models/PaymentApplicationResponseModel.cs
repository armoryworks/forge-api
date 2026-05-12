namespace Forge.Core.Models;

public record PaymentApplicationResponseModel(
    int Id,
    int PaymentId,
    string? PaymentNumber,
    int InvoiceId,
    string? InvoiceNumber,
    decimal Amount);
