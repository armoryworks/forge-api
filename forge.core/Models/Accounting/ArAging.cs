namespace Forge.Core.Models.Accounting;

/// <summary>
/// AR-002 — AR sub-ledger aging report. The report is <b>derived from the
/// open-item sub-ledger</b> (<c>ArOpenItem</c> rows maintained at posting time,
/// in the same transaction as every AR-control journal): per customer, the open
/// functional remainder of each non-Closed/non-Voided document, bucketed by the
/// age of its DueDate (DocumentDate when no due date is set) — document-grain
/// aging, the standard AR treatment (formerly balance-forward at the posting
/// grain). The reconciliation in <see cref="ArAgingReconciliation"/> ties the
/// total back to the AR-control GL balance. Amounts are <b>functional</b>
/// currency at each document's booking rate.
/// </summary>

/// <summary>
/// One age bucket of a customer's open AR balance. Buckets are computed from
/// the age (in days) of each open item's due date (document date fallback)
/// relative to the report's as-of date; each invoice's OPEN remainder
/// (original − applied) lands wholly in its document's bucket, so a partial
/// payment shrinks that bucket rather than crediting a younger one. Documents
/// not yet due sit in the youngest bucket.
/// </summary>
public sealed class ArAgingBucket
{
    /// <summary>Inclusive lower age bound in days (e.g. 0, 31, 61, 91).</summary>
    public int FromDays { get; init; }

    /// <summary>
    /// Inclusive upper age bound in days; null = open-ended (the oldest bucket,
    /// e.g. "91+").
    /// </summary>
    public int? ToDays { get; init; }

    /// <summary>Human-readable label, e.g. "0-30", "31-60", "91+".</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>Net open amount (functional) attributable to this bucket.</summary>
    public decimal Amount { get; init; }
}

/// <summary>
/// A single customer's open AR balance, broken into age buckets. The
/// <see cref="OpenBalance"/> is the sum of the customer's open-item remainders
/// (original − applied, functional at booking rates) and equals the sum of the
/// bucket amounts.
/// </summary>
public sealed class ArAgingCustomerRow
{
    public int CustomerId { get; init; }
    public string CustomerName { get; init; } = string.Empty;

    /// <summary>Net open receivable (functional) = Σ bucket amounts = Σ open items.</summary>
    public decimal OpenBalance { get; init; }

    public IReadOnlyList<ArAgingBucket> Buckets { get; init; } = [];
}

/// <summary>
/// The AR sub-ledger aging report for a book as of a date. Rows are one per
/// customer with a non-zero open balance; <see cref="TotalsByBucket"/> rolls the
/// buckets up across customers and <see cref="GrandTotal"/> is the total open AR.
/// </summary>
public sealed class ArAging
{
    public int BookId { get; init; }

    /// <summary>The date the aging is computed against (default = today).</summary>
    public DateOnly AsOfDate { get; init; }

    public IReadOnlyList<ArAgingCustomerRow> Customers { get; init; } = [];

    /// <summary>Bucket totals rolled up across all customers (same bucket order).</summary>
    public IReadOnlyList<ArAgingBucket> TotalsByBucket { get; init; } = [];

    /// <summary>Total open AR (functional) = Σ customer open balances.</summary>
    public decimal GrandTotal { get; init; }

    /// <summary>
    /// Reconciliation of the aging total against the AR-control account balance
    /// in the GL. Always populated — the aging is only trustworthy if it ties.
    /// </summary>
    public ArAgingReconciliation Reconciliation { get; init; } = new();
}

/// <summary>
/// AR-control-vs-open-items reconciliation (§9 "sub-ledger↔control
/// reconciliation"). Compares Σ open functional amounts of the open items
/// (<see cref="AgingTotal"/>) to the posted balance of the AR-control GL
/// account(s) (<see cref="ControlBalance"/>). The items are maintained inside
/// the same transactions that move control, so they tie exactly; a non-zero
/// <see cref="Difference"/> means a manual/conversion JE hit AR control
/// directly (bypasses items by design — this row surfaces it), a legacy
/// document awaits the boot-time backfill, or an out-of-band mutation occurred.
/// A Voided item counts on neither side (its GL was reversed).
/// </summary>
public sealed class ArAgingReconciliation
{
    /// <summary>Net AR-control account balance from the GL (Dr − Cr, functional).</summary>
    public decimal ControlBalance { get; init; }

    /// <summary>Σ open functional amounts of the AR open items (the sub-ledger side).</summary>
    public decimal AgingTotal { get; init; }

    /// <summary>
    /// ControlBalance − AgingTotal. Zero when the open-item sub-ledger ties to
    /// the control account; non-zero is alertable (see class doc).
    /// </summary>
    public decimal Difference => ControlBalance - AgingTotal;

    /// <summary>True when the sub-ledger ties to the AR-control account.</summary>
    public bool IsReconciled => Difference == 0m;
}
