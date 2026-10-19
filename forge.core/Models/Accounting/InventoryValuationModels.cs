namespace Forge.Core.Models.Accounting;

/// <summary>One part's on-hand valuation row.</summary>
public sealed record InventoryValuationModel(
    int PartId,
    string PartNumber,
    decimal OnHandQuantity,
    decimal AverageUnitCost,
    decimal TotalValue);

/// <summary>
/// ⚡ STAGE E reconciliation (§9): the valuation sub-ledger total vs the GL INVENTORY_* control balance.
/// <see cref="GlInventoryBalance"/> is every inventory control account, work in process included; the store
/// holds stocked parts only, so <see cref="Variance"/> compares it with the stocked accounts
/// (<see cref="GlStockedBalance"/>) and open-job WIP is reported on its own. A non-zero variance flags drift
/// (e.g. an opening journal or a manual JE to an inventory account that didn't go through the store), and
/// <see cref="ReconcilingItems"/> lists what makes it up.
/// </summary>
public sealed record InventoryValuationReconciliation(
    int BookId,
    decimal StoreValue,
    decimal GlInventoryBalance,
    decimal RoundingTolerance)
{
    public decimal GlWorkInProcessBalance { get; init; }
    public decimal GlStockedBalance => GlInventoryBalance - GlWorkInProcessBalance;
    public decimal Variance => StoreValue - GlStockedBalance;
    public bool IsReconciled => Math.Abs(Variance) <= RoundingTolerance;
    public IReadOnlyList<InventoryReconciliationAccountLine> Accounts { get; init; } = [];
    public IReadOnlyList<InventoryReconcilingItem> ReconcilingItems { get; init; } = [];
    public IReadOnlyList<InventoryWorkInProcessByJob> WorkInProcessByJob { get; init; } = [];
}
