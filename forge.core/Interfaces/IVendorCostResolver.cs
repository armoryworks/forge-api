using Forge.Core.Models;

namespace Forge.Core.Interfaces;

/// <summary>
/// UoM purchase-units effort — derives a part's cost per base/stock unit from the preferred
/// vendor's price tiers and the chosen purchase unit's content quantity. Picks the option that
/// is cheapest per base unit for the requested quantity (quantity-break aware), or the lone
/// per-base-unit tier when the part has no purchase units (legacy single-option behavior).
/// </summary>
public interface IVendorCostResolver
{
    /// <param name="requestedBaseQty">Quantity needed in the part's base/stock UoM (drives the
    /// quantity-break selection; defaults to 1 when ≤ 0).</param>
    Task<ResolvedBaseUnitCost> ResolveAsync(int partId, decimal requestedBaseQty, CancellationToken ct);

    /// <summary>Reverse direction (UI bidirectional): the per-option price implied by a target
    /// per-base-unit cost — <c>costPerBaseUnit × contentQuantity</c>.</summary>
    static decimal OptionPriceFromBaseUnitCost(decimal costPerBaseUnit, decimal contentQuantity)
        => costPerBaseUnit * contentQuantity;
}
