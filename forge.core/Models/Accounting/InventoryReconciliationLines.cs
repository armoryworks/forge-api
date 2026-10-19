namespace Forge.Core.Models.Accounting;

/// <summary>One stocked inventory control account: its GL balance beside the valuation-store value of the parts
/// whose inventory class maps to it.</summary>
public sealed record InventoryReconciliationAccountLine(
    string AccountNumber,
    string AccountName,
    decimal StoreValue,
    decimal GlBalance)
{
    public decimal Variance => StoreValue - GlBalance;
}

/// <summary>
/// One reason the valuation store and the stocked GL inventory accounts differ. <see cref="Kind"/> is
/// <c>PostedOutsideStore</c> for GL inventory amounts from a source that never feeds the store (an opening
/// journal, an opening balance, a manual entry), or <c>Unexplained</c> for the remainder on an account. The
/// items sum to <see cref="InventoryValuationReconciliation.Variance"/>.
/// </summary>
public sealed record InventoryReconcilingItem(
    string Kind,
    string? AccountNumber,
    string? SourceType,
    decimal Amount,
    string Description);

/// <summary>One job's GL work-in-process balance (the WIP lines tagged with the job).</summary>
public sealed record InventoryWorkInProcessByJob(int? JobId, decimal Amount);
