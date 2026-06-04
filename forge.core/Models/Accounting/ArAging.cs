namespace Forge.Core.Models.Accounting;

/// <summary>
/// Phase-1 STAGE D — AR sub-ledger + aging report (ACCOUNTING_SUITE_PLAN §6
/// Phase-1 row "AR sub-ledger + aging", §7 matrix rows 1–3). The report is
/// <b>derived from the ledger</b> — it sums posted <c>JournalLine</c>s on
/// AR-control accounts (<c>GlAccount.ControlType == AR</c>) carrying
/// <c>SubledgerPartyType = Customer</c> — so it is always tied to the GL by
/// construction (the reconciliation in <see cref="ArAgingReconciliation"/>
/// proves it). Amounts are <b>functional</b> currency (Phase-0/1 single-currency
/// invariant — TxnAmount == FunctionalAmount).
/// </summary>

/// <summary>
/// One age bucket of a customer's open AR balance. Buckets are computed from the
/// age (in days) of each posting's <c>EntryDate</c> relative to the report's
/// as-of date. A debit on the AR control line increases the open balance
/// (an invoice); a credit decreases it (a payment / credit memo applied to the
/// customer). The aging signs net amounts within each bucket so a fully-paid
/// customer ages to zero.
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
/// <see cref="OpenBalance"/> is the net of all AR-control postings for the
/// customer (debits − credits) and equals the sum of the bucket amounts.
/// </summary>
public sealed class ArAgingCustomerRow
{
    public int CustomerId { get; init; }
    public string CustomerName { get; init; } = string.Empty;

    /// <summary>Net open receivable (functional) = Σ bucket amounts = Dr − Cr.</summary>
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
/// AR-control-vs-aging reconciliation (§9 "sub-ledger↔control reconciliation").
/// Compares the sum of the derived aging (<see cref="AgingTotal"/>) to the
/// posted balance of the AR-control GL account(s) (<see cref="ControlBalance"/>).
/// They must be equal because the aging is derived from the very same AR-control
/// <c>JournalLine</c>s; a non-zero <see cref="Difference"/> means some AR posting
/// is missing a customer party (so it aged into no customer) or an out-of-band
/// mutation occurred — i.e. a bug to alert on.
/// </summary>
public sealed class ArAgingReconciliation
{
    /// <summary>Net AR-control account balance from the GL (Dr − Cr, functional).</summary>
    public decimal ControlBalance { get; init; }

    /// <summary>Sum of the customer-attributed aging (functional).</summary>
    public decimal AgingTotal { get; init; }

    /// <summary>
    /// ControlBalance − AgingTotal. Zero when the sub-ledger ties to the control
    /// account. The portion of the control balance carrying no
    /// <c>SubledgerPartyType = Customer</c> party (which the engine should never
    /// allow on a control line) is exactly this difference.
    /// </summary>
    public decimal Difference => ControlBalance - AgingTotal;

    /// <summary>True when the sub-ledger ties to the AR-control account.</summary>
    public bool IsReconciled => Difference == 0m;
}
