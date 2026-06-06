namespace Forge.Core.Models;

/// <summary>
/// ⚡ ACCOUNTING BOUNDARY — request to create a <c>VendorPayment</c> (AP counterpart of
/// <c>CreatePaymentRequestModel</c>). Creating a vendor payment IS the cash-disbursement posting trigger.
/// </summary>
public record CreateVendorPaymentRequestModel(
    int VendorId,
    string Method,
    decimal Amount,
    DateTimeOffset PaymentDate,
    string? ReferenceNumber,
    string? Notes,
    List<CreateVendorPaymentApplicationModel>? Applications);

/// <summary>Links part of a vendor payment to a specific vendor bill.</summary>
public record CreateVendorPaymentApplicationModel(int VendorBillId, decimal Amount);
