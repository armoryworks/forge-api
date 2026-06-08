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
/// <remarks>
/// <c>SettlementFxRate</c> (txn→functional, default 1) is the rate in effect when this payment settled the
/// bill; the cash-disbursement posting realizes FX vs the bill's booking rate. Default keeps single-currency
/// settlements unchanged.
/// </remarks>
public record CreateVendorPaymentApplicationModel(int VendorBillId, decimal Amount, decimal SettlementFxRate = 1m);
