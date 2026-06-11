namespace Forge.Core.Models;

/// <summary>
/// ⚡ ACCOUNTING BOUNDARY — list/detail projection of a <c>VendorPayment</c> (AP counterpart of
/// <c>PaymentListItemModel</c>). The Transmission* fields reflect the LATEST bank transmission for
/// electronic payments (null/0 when none — cash/check payments are never transmitted).
/// </summary>
public record VendorPaymentListItemModel(
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
    DateTimeOffset CreatedAt,
    string? TransmissionStatus = null,
    int TransmissionAttempts = 0,
    int? TransmissionId = null);
