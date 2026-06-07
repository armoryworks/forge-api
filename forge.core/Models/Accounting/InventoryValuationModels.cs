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
/// A non-zero variance flags drift (e.g. receipts fed but the relief side not yet wired, or a manual JE to
/// an inventory account that didn't go through the store).
/// </summary>
public sealed record InventoryValuationReconciliation(
    int BookId,
    decimal StoreValue,
    decimal GlInventoryBalance,
    decimal RoundingTolerance)
{
    public decimal Variance => StoreValue - GlInventoryBalance;
    public bool IsReconciled => Math.Abs(Variance) <= RoundingTolerance;
}
