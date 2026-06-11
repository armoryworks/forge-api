namespace Forge.Core.Models;

/// <summary>
/// ⚡ ACCOUNTING BOUNDARY — full detail projection of a <c>VendorPayment</c>: the list shape plus the per-bill
/// application breakdown, so the payment detail screen can show which bills the payment settled.
/// The Transmission* block mirrors the LATEST bank transmission (all null/0 for non-electronic payments)
/// so the detail dialog can render the submission state without an extra round-trip.
/// </summary>
public record VendorPaymentDetailModel(
    int Id,
    string PaymentNumber,
    int VendorId,
    string VendorName,
    string Method,
    decimal Amount,
    decimal AppliedAmount,
    decimal UnappliedAmount,
    DateTimeOffset PaymentDate,
    string? ReferenceNumber,
    string? Notes,
    DateTimeOffset CreatedAt,
    IReadOnlyList<VendorPaymentApplicationDetailModel> Applications,
    int? TransmissionId = null,
    string? TransmissionStatus = null,
    int TransmissionAttempts = 0,
    string? TransmissionLastError = null,
    string? TransmissionSubmissionRef = null,
    DateTimeOffset? TransmissionNextAttemptAt = null);
