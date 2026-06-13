using Forge.Core.Enums.Accounting;

namespace Forge.Core.Entities.Accounting;

/// <summary>
/// One open item in the AR sub-ledger — a per-document (invoice-grain) receivable record,
/// maintained <b>at posting time inside the same transaction as the control-account journal</b>
/// (the posting services are the single seam where AR control moves, which is what keeps the
/// items reconciled to control by construction):
/// <list type="bullet">
///   <item><c>InvoiceArPostingService</c> creates the item when it posts the Dr AR_CONTROL
///         revenue entry (txn = the posted invoice total in its currency, functional = txn ×
///         booking <see cref="FxRate"/>, rounded exactly like the posting).</item>
///   <item><c>PaymentCashPostingService</c> increments <see cref="AppliedTxnAmount"/> /
///         <see cref="AppliedFunctionalAmount"/> per application by the exact AR-relief amounts
///         it posts (foreign amount + relief at the BOOKING rate — never the settlement rate;
///         realized FX is the payment entry's plug, not the item's).</item>
///   <item><c>VoidPayment</c> (via the cash posting service's reversal) decrements the applied
///         amounts back (floored at zero) so the item reopens with the GL.</item>
/// </list>
/// <para>Amounts: <see cref="OriginalTxnAmount"/> is in the document's currency
/// (<see cref="CurrencyId"/>); functional amounts are in the book's functional currency at the
/// document's booking rate. A <see cref="OpenItemStatus.Voided"/> item is excluded from aging
/// and from the sub-ledger side of the control reconciliation (the reversed GL contributes to
/// neither side) — see <see cref="OpenItemStatus"/>.</para>
/// </summary>
public class ArOpenItem : BaseEntity
{
    public int BookId { get; set; }

    /// <summary>The customer that owes the receivable (the sub-ledger party).</summary>
    public int CustomerId { get; set; }

    /// <summary>Source-document type — "Invoice" (matches the posting idempotency key shape).</summary>
    public string SourceType { get; set; } = "Invoice";

    /// <summary>Source-document id (the Invoice id).</summary>
    public int SourceId { get; set; }

    /// <summary>Human-readable document number (e.g. the invoice number) for reporting.</summary>
    public string DocumentNumber { get; set; } = string.Empty;

    /// <summary>The document's own date (Invoice.InvoiceDate) — the aging fallback when no due date is set.</summary>
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
