namespace Forge.Core.Models;

/// <summary>
/// Identifies which rung of <see cref="Forge.Core.Interfaces.IPartPricingResolver"/>
/// produced a resolved price.
/// </summary>
public enum PartPriceSource
{
    /// <summary>Customer-scoped <c>PriceListEntry</c> matched on quantity tier.</summary>
    PriceListEntry,

    /// <summary>Part-level effective-dated default sales price (<c>PartPrice</c>).</summary>
    PartPrice,

    /// <summary>Preferred vendor's lowest-MinQuantity <c>VendorPartPriceTier</c>.</summary>
    VendorPartTier,

    /// <summary>No source resolved — price defaults to 0.</summary>
    Default,
}
