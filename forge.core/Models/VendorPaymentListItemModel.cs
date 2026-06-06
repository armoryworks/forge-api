namespace Forge.Core.Models;

/// <summary>
/// ⚡ ACCOUNTING BOUNDARY — list/detail projection of a <c>VendorPayment</c> (AP counterpart of
/// <c>PaymentListItemModel</c>).
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
    DateTimeOffset CreatedAt);
