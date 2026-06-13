namespace Forge.Core.Models.Accounting;

/// <summary>
/// ⚡ Accounts-Payable aging report (the AP counterpart of <c>ArAging</c>). Derived from the
/// <b>open-item sub-ledger</b> (<c>ApOpenItem</c> rows maintained at posting time): per vendor,
/// each non-Closed/non-Voided bill's open functional remainder, bucketed by the age of its
/// DueDate (BillDate fallback) — document-grain aging (formerly balance-forward at the posting
/// grain). An open payable is a positive open balance.
/// </summary>
public sealed class ApAging
{
    public int BookId { get; init; }
    public DateOnly AsOfDate { get; init; }
    public IReadOnlyList<ApAgingVendorRow> Vendors { get; init; } = [];
    public IReadOnlyList<ApAgingBucket> TotalsByBucket { get; init; } = [];
    public decimal GrandTotal { get; init; }
    public ApAgingReconciliation Reconciliation { get; init; } = new();
}

/// <summary>One vendor's open payable, split across the aging buckets.</summary>
public sealed class ApAgingVendorRow
{
    public int VendorId { get; init; }
    public string VendorName { get; init; } = string.Empty;
    public decimal OpenBalance { get; init; }
    public IReadOnlyList<ApAgingBucket> Buckets { get; init; } = [];
}

/// <summary>An aging bucket (e.g. 0-30, 31-60, 61-90, 91+).</summary>
public sealed class ApAgingBucket
{
    public int FromDays { get; init; }
    public int? ToDays { get; init; }
    public string Label { get; init; } = string.Empty;
    public decimal Amount { get; init; }
}

/// <summary>
/// Ties Σ open functional amounts of the AP open items back to the full GL AP-control balance.
/// A non-zero <see cref="Difference"/> means AP control moved without an open item — a manual JE
/// posted directly to AP control (bypasses items by design; surfaced here), a vendor-settled
/// Expense posting (credits AP control without a bill document), or a legacy document awaiting
/// the boot-time backfill. A Voided item counts on neither side (its GL was reversed).
/// </summary>
public sealed class ApAgingReconciliation
{
    public decimal ControlBalance { get; init; }
    public decimal AgingTotal { get; init; }
    public decimal Difference => ControlBalance - AgingTotal;
    public bool IsReconciled => Difference == 0m;
}
