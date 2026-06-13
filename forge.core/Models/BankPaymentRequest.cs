namespace Forge.Core.Models;

/// <summary>
/// Request to submit one electronic payment (ACH / wire) to the bank channel. Generic over any
/// transaction source — (<paramref name="SourceType"/>, <paramref name="SourceId"/>) mirror the
/// originating <c>PaymentTransmission</c>.
/// </summary>
public record BankPaymentRequest(
    string SourceType,
    int SourceId,
    decimal Amount,
    string Method,
    string? ReferenceNumber,
    string? VendorName);
