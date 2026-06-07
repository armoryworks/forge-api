using Forge.Core.Models.Accounting;

namespace Forge.Core.Interfaces;

/// <summary>
/// ⚡ Phase-2 STAGE E — the perpetual inventory valuation store (per Book+Part). Receipts feed it; issues
/// relieve it at the running weighted-average; it reconciles to the GL INVENTORY_* control balance.
/// </summary>
public interface IInventoryValuationService
{
    /// <summary>Adds received quantity + landed cost to the part's running average (creates the row if new).</summary>
    Task ApplyReceiptAsync(int bookId, int partId, decimal quantity, decimal totalCost, CancellationToken ct = default);

    /// <summary>Relieves quantity at the running average; returns the relieved value (the COGS basis).</summary>
    Task<decimal> ApplyIssueAsync(int bookId, int partId, decimal quantity, CancellationToken ct = default);

    Task<IReadOnlyList<InventoryValuationModel>> GetAsync(int bookId, CancellationToken ct = default);

    /// <summary>Ties the store total to the GL inventory-control balance (§9).</summary>
    Task<InventoryValuationReconciliation> ReconcileAsync(int bookId, CancellationToken ct = default);
}
