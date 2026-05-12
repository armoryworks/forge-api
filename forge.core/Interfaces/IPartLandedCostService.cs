using Forge.Core.Models;

namespace Forge.Core.Interfaces;

/// <summary>
/// Computes landed unit cost for a part by aggregating its receipt history.
/// Bought-parts effort PR3.
///
/// <para>Landed unit cost = base unit price + allocated freight + duty +
/// FX adjustment, all per unit, in the tenant's functional currency.
/// Per-receipt landed cost is exact (the components for that receipt);
/// the part's "current" landed cost is averaged over the most recent N
/// receipts (default 3, configurable). Above the average, the cost tab
/// shows a vendor comparison so the buyer can spot a cheaper source at
/// a glance.</para>
///
/// <para>Cost is only meaningful for procurement-source <c>Buy</c> /
/// <c>Subcontract</c> parts. The handler returns an empty
/// <c>RecentReceipts</c> list for parts without receipt history;
/// <c>AverageLandedUnitCost</c> is null in that case.</para>
/// </summary>
public interface IPartLandedCostService
{
    /// <summary>
    /// Compute the landed-cost surface for a part. Default <paramref name="maxReceipts"/>
    /// is 3 — picks the most recent receipts that have ActualFreight
    /// captured (records with null AllocatedFreight aren't averaged).
    /// </summary>
    Task<PartLandedCostResponseModel> GetForPartAsync(
        int partId,
        int maxReceipts,
        CancellationToken ct);
}
