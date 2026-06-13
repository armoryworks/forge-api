using Forge.Core.Enums.Accounting;

namespace Forge.Core.Entities.Accounting;

/// <summary>
/// One open item in the AP sub-ledger — the vendor-side mirror of <see cref="ArOpenItem"/>:
/// a per-document (vendor-bill-grain) payable record maintained <b>at posting time inside the
/// same transaction as the control-account journal</b>:
/// <list type="bullet">
///   <item><c>VendorBillApPostingService</c> creates the item when it posts the Cr AP_CONTROL
///         approval entry (txn = the posted bill total in its currency, functional = txn ×
///         booking <see cref="FxRate"/>, rounded exactly like the posting).</item>
///   <item><c>VendorPaymentCashPostingService</c> increments <see cref="AppliedTxnAmount"/> /
///         <see cref="AppliedFunctionalAmount"/> per application by the exact AP-relief amounts
///         it posts (foreign amount + relief at the BOOKING rate — never the settlement rate).</item>
///   <item><c>VoidVendorPayment</c> (via the cash posting service's reversal) decrements the
///         applied amounts back (floored at zero) so the item reopens with the GL.</item>
///   <item><c>VoidVendorBill</c> (Approved-bill reversal path) flips the item to
///         <see cref="OpenItemStatus.Voided"/> — logically zeroing it: a Voided item is
///         excluded from aging AND the reconciliation adds it to neither side, matching the
///         reversed GL (the bill's AP control nets to zero).</item>
/// </list>
/// </summary>
public class ApOpenItem : BaseEntity
{
    public int BookId { get; set; }

    /// <summary>The vendor owed the payable (the sub-ledger party).</summary>
    public int VendorId { get; set; }

    /// <summary>Source-document type — "VendorBill" (matches the posting idempotency key shape).</summary>
    public string SourceType { get; set; } = "VendorBill";

    /// <summary>Source-document id (the VendorBill id).</summary>
    public int SourceId { get; set; }

    /// <summary>Human-readable document number (e.g. the bill number) for reporting.</summary>
    public string DocumentNumber { get; set; } = string.Empty;

    /// <summary>The document's own date (VendorBill.BillDate) — the aging fallback when no due date is set.</summary>
    public DateTimeOffset DocumentDate { get; set; }

    /// <summary>Due date (terms) — the primary aging anchor.</summary>
    public DateTimeOffset DueDate { get; set; }

    /// <summary>Transaction currency of the document.</summary>
    public int CurrencyId { get; set; }

    /// <summary>Booking FX rate snapshot (transaction → functional) — the document's rate when posted.</summary>
    public decimal FxRate { get; set; } = 1m;

    /// <summary>Posted document total in the transaction currency.</summary>
    public decimal OriginalTxnAmount { get; set; }

    /// <summary>Posted document total in functional currency (txn × booking rate, rounded like the posting).</summary>
    public decimal OriginalFunctionalAmount { get; set; }

    /// <summary>Σ applied (settled) amount in the transaction currency.</summary>
    public decimal AppliedTxnAmount { get; set; }

    /// <summary>Σ applied (settled) amount in functional currency, at the BOOKING rate.</summary>
    public decimal AppliedFunctionalAmount { get; set; }

    public OpenItemStatus Status { get; set; } = OpenItemStatus.Open;

    /// <summary>Outstanding amount in the transaction currency (computed — not mapped).</summary>
    public decimal OpenTxnAmount => OriginalTxnAmount - AppliedTxnAmount;

    /// <summary>Outstanding amount in functional currency at the booking rate (computed — not mapped).</summary>
    public decimal OpenFunctionalAmount => OriginalFunctionalAmount - AppliedFunctionalAmount;

    /// <summary>
    /// Recomputes <see cref="Status"/> from the applied-vs-original transaction amounts:
    /// applied == 0 → Open; &lt; original → PartiallyApplied; ≥ original → Closed.
    /// A <see cref="OpenItemStatus.Voided"/> item stays Voided (terminal — the document's GL
    /// was reversed; applications can no longer move it).
    /// </summary>
    public void RecomputeStatus()
    {
        if (Status == OpenItemStatus.Voided)
            return;

        Status = AppliedTxnAmount <= 0m
            ? OpenItemStatus.Open
            : AppliedTxnAmount < OriginalTxnAmount
                ? OpenItemStatus.PartiallyApplied
                : OpenItemStatus.Closed;
    }
}
