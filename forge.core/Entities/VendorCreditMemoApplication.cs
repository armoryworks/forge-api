namespace Forge.Core.Entities;

/// <summary>
/// ⚡ ACCOUNTING BOUNDARY — part of a vendor credit memo used against a bill, in the shared document currency.
/// Unapplying stamps <see cref="ReversedAt"/>; the row stays for the audit trail. An application a payment or a payment
/// run made names it, so a resumed run can tell what it already did.
/// </summary>
public class VendorCreditMemoApplication : BaseEntity
{
    public int VendorCreditMemoId { get; set; }

    public int VendorBillId { get; set; }

    public decimal Amount { get; set; }

    public DateTimeOffset AppliedAt { get; set; }

    public int? AppliedByUserId { get; set; }

    public DateTimeOffset? ReversedAt { get; set; }

    public int? ReversedByUserId { get; set; }

    /// <summary>The vendor payment the credit was deducted from, when it was applied with one.</summary>
    public int? VendorPaymentId { get; set; }

    /// <summary>The payment run that applied the credit, when one did.</summary>
    public int? PaymentRunId { get; set; }

    public bool IsLive => ReversedAt is null;

    public VendorCreditMemo VendorCreditMemo { get; set; } = null!;
    public VendorBill VendorBill { get; set; } = null!;
}
