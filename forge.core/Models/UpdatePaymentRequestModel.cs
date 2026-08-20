namespace Forge.Core.Models;

/// <summary>P06-5: payload for amending a recorded payment (policy-gated).</summary>
public record UpdatePaymentRequestModel(
    string Method,
    decimal Amount,
    DateTimeOffset PaymentDate,
    string? ReferenceNumber,
    string? Notes,
    // Optional caller-supplied payment number. Editable only while the payment has no
    // applications and the payments.allow_manual_numbers setting is on (see UpdatePaymentHandler).
    string? PaymentNumber = null);
