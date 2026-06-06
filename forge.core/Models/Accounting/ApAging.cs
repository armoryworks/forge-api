namespace Forge.Core.Models.Accounting;

/// <summary>
/// ⚡ Accounts-Payable aging report (the AP counterpart of <c>ArAging</c>). Ages the AP control
/// account by vendor party at the <c>JournalEntry.EntryDate</c> grain (balance-forward, NOT FIFO
/// open-item application — matches the AR aging model exactly). AP is credit-normal, so an open
/// payable is a positive open balance (credit-positive netting).
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
/// Ties the aging total (vendor-attributed AP-control lines) back to the full GL AP-control balance.
/// A non-zero <see cref="Difference"/> means AP-control postings exist without a vendor party.
/// </summary>
public sealed class ApAgingReconciliation
{
    public decimal ControlBalance { get; init; }
    public decimal AgingTotal { get; init; }
    public decimal Difference => ControlBalance - AgingTotal;
    public bool IsReconciled => Difference == 0m;
}
