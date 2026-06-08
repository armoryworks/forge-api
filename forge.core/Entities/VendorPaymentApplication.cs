namespace Forge.Core.Entities;

/// <summary>
/// ⚡ ACCOUNTING BOUNDARY — links a <see cref="VendorPayment"/> to a specific <see cref="VendorBill"/>
/// (the AP counterpart of <see cref="PaymentApplication"/>). Enables partial payments and overpayments.
/// </summary>
public class VendorPaymentApplication : BaseEntity
{
    public int VendorPaymentId { get; set; }
    public int VendorBillId { get; set; }
    public decimal Amount { get; set; }

    /// <summary>
    /// FX rate (transaction→functional) in effect when this payment settled the bill. Default 1
    /// (single-currency). The cash-disbursement posting relieves AP at the bill's BOOKING rate and pays
    /// cash out at this SETTLEMENT rate, plugging the difference to realized FX gain/loss.
    /// </summary>
    public decimal SettlementFxRate { get; set; } = 1m;

    public VendorPayment VendorPayment { get; set; } = null!;
    public VendorBill VendorBill { get; set; } = null!;
}
