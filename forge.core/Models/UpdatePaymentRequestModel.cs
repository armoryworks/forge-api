namespace Forge.Core.Models;

/// <summary>P06-5: payload for amending a recorded payment (policy-gated).</summary>
public record UpdatePaymentRequestModel(
    string Method,
    decimal Amount,
    DateTimeOffset PaymentDate,
    string? ReferenceNumber,
    string? Notes);
