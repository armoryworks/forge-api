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

    public VendorPayment VendorPayment { get; set; } = null!;
    public VendorBill VendorBill { get; set; } = null!;
}
