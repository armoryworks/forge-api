namespace Forge.Core.Entities.Accounting;

/// <summary>
/// ⚡ Phase-2 STAGE E — per-(Book, Part) inventory valuation (the perpetual inventory sub-ledger that ties to
/// the GL INVENTORY_* control accounts, §8.1 / §9). One row per stocked part per book: on-hand quantity, a
/// running weighted-average unit cost, and total value. Receipts increment it (STAGE C feed); issues/relief
/// decrement it at the running average (the <c>ApplyIssue</c> seam — wired when the operational inventory
/// movements land). Standard/FIFO costing are method-driven extensions of this store (Book.DefaultCostingMethod).
/// </summary>
public class InventoryValuation : BaseEntity
{
    public int BookId { get; set; }
    public int PartId { get; set; }

    public decimal OnHandQuantity { get; set; }

    /// <summary>Running weighted-average unit cost.</summary>
    public decimal AverageUnitCost { get; set; }

    /// <summary>On-hand value (kept equal to OnHandQuantity × AverageUnitCost, currency-rounded).</summary>
    public decimal TotalValue { get; set; }

    /// <summary>Optimistic-locking token (receipt/issue races).</summary>
    public uint Version { get; set; }

    public Part Part { get; set; } = null!;
}
