namespace Forge.Core.Models.Accounting;

/// <summary>
/// ⚡ Phase-2 STAGE D.3 — Goods-Received-Not-Invoiced (GRNI) reconciliation + aging. GRNI is a clearing
/// liability, NOT a control account (no sub-ledger party), so it can't be aged by party like AP/AR. Instead
/// it ties two independent sources of truth for "received-but-not-yet-billed":
/// <list type="bullet">
///   <item><b>GL balance</b> — the net GRNI account balance from the ledger (credit-normal: Cr − Dr over the
///         accounts mapped to the <c>GRNI</c> determination key, Posted + Reversed, on/before <c>AsOf</c>).
///         Built up by receipt accruals (STAGE C, Cr) and drawn down by 3-way-match bill clears (STAGE D.2,
///         Dr).</item>
///   <item><b>Operational open</b> — Σ over purchase-order lines of
///         <c>UnbilledReceivedQuantity × UnitPrice</c> (received minus billed, at PO price — exactly what the
///         receipt accrued and the bill clears). This is the model the aging buckets are built from, aged by
///         each line's earliest receipt date.</item>
/// </list>
/// When FULLGL is on and every receipt was posted and every bill matched, the two are equal; a non-zero
/// <see cref="Variance"/> is the §12 reconciliation signal (an un-posted receipt, the second receive path
/// that doesn't accrue GRNI, freight/rounding drift, or receipts predating FULLGL). <see cref="UncoveredReceipts"/>
/// is the line-level drill-down: receiving records with no GRNI accrual posting.
/// </summary>
public sealed class GrniReconciliation
{
    public int BookId { get; init; }
    public DateOnly AsOfDate { get; init; }

    /// <summary>Net GRNI balance from the GL (credit-positive — an open accrual is positive).</summary>
    public decimal GlBalance { get; init; }

    /// <summary>Operational open GRNI: Σ <c>UnbilledReceivedQuantity × PO UnitPrice</c>.</summary>
    public decimal OperationalOpen { get; init; }

    /// <summary>GL minus operational. Zero when the ledger and the receive/bill state agree.</summary>
    public decimal Variance => GlBalance - OperationalOpen;

    /// <summary>
    /// The book's rounding tolerance. The GL side is posted at currency scale while the operational side is
    /// computed fresh at full decimal precision, so fractional receipt quantities can leave a sub-cent
    /// residue that is NOT a real break — <see cref="IsReconciled"/> absorbs it within this tolerance.
    /// </summary>
    public decimal RoundingTolerance { get; init; }

    public bool IsReconciled => Math.Abs(Variance) <= RoundingTolerance;

    /// <summary>Open GRNI per purchase order, aged. Sorted by open amount descending.</summary>
    public IReadOnlyList<GrniPoRow> PurchaseOrders { get; init; } = [];

    public IReadOnlyList<GrniAgingBucket> TotalsByBucket { get; init; } = [];

    /// <summary>Σ of the per-PO open amounts (the operational open, from the aging side).</summary>
    public decimal GrandTotal { get; init; }

    /// <summary>
    /// Receiving records (on/before <c>AsOf</c>) with no matching GRNI accrual journal entry — i.e. goods
    /// received but never accrued. Bounded to <see cref="UncoveredReceiptLimit"/>; <see cref="UncoveredTruncated"/>
    /// flags when more exist. (Receipts predating the FULLGL switch-on legitimately appear here until the
    /// §7A opening conversion — read with that in mind.)
    /// </summary>
    public IReadOnlyList<GrniUncoveredReceipt> UncoveredReceipts { get; init; } = [];

    public const int UncoveredReceiptLimit = 200;
    public bool UncoveredTruncated { get; init; }
}

/// <summary>One purchase order's open GRNI, split across the aging buckets (aged by earliest receipt date).</summary>
public sealed class GrniPoRow
{
    public int PurchaseOrderId { get; init; }
    public string PoNumber { get; init; } = string.Empty;
    public int VendorId { get; init; }
    public string VendorName { get; init; } = string.Empty;
    public decimal OpenAmount { get; init; }
    public IReadOnlyList<GrniAgingBucket> Buckets { get; init; } = [];
}

/// <summary>An aging bucket (0-30, 31-60, 61-90, 91+), matching the AP/AR bucket grain.</summary>
public sealed class GrniAgingBucket
{
    public int FromDays { get; init; }
    public int? ToDays { get; init; }
    public string Label { get; init; } = string.Empty;
    public decimal Amount { get; init; }
}

/// <summary>A received-but-not-GRNI-accrued receiving record (line-level coverage drill-down).</summary>
public sealed class GrniUncoveredReceipt
{
    public int ReceivingRecordId { get; init; }
    public int PurchaseOrderId { get; init; }
    public int PurchaseOrderLineId { get; init; }
    public string? ReceiptNumber { get; init; }
    public decimal QuantityReceived { get; init; }
    public DateOnly ReceivedDate { get; init; }
    /// <summary>Why it's uncovered: <c>NO_RECEIPT_NUMBER</c> (un-postable) or <c>NO_ACCRUAL_POSTED</c>.</summary>
    public string Reason { get; init; } = string.Empty;
}
