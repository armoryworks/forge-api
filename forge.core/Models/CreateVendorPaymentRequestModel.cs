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
    List<CreateVendorPaymentApplicationModel>? Applications,
    int? CurrencyId = null,
    decimal? FxRate = null);

/// <summary>Links part of a vendor payment to a specific vendor bill.</summary>
/// <remarks>
/// <c>SettlementFxRate</c> (txn→functional) is the rate in effect when this payment settled the bill; null
/// takes the payment's rate. The cash-disbursement posting realizes FX vs the bill's booking rate.
/// <c>DiscountAmount</c> is the early-payment discount taken: null takes the automatic discount when the payment
/// date is in the bill's discount window, 0 takes none, and an explicit amount outside the window or above the
/// terms' maximum needs <c>AllowDiscountOverride</c> from an Admin or Manager.
/// </remarks>
public record CreateVendorPaymentApplicationModel(
    int VendorBillId,
    decimal Amount,
    decimal? SettlementFxRate = null,
    decimal? DiscountAmount = null,
    bool AllowDiscountOverride = false);

/// <summary>
/// An open vendor credit memo to deduct with a payment: <c>Amount</c> of the memo is applied to the bill in the same
/// transaction, before the cash.
/// </summary>
public record CreateVendorPaymentCreditModel(int VendorCreditMemoId, int VendorBillId, decimal Amount);
