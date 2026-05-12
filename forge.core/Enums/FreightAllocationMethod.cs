namespace Forge.Core.Enums;

/// <summary>
/// How a receipt's `ActualFreight` is split across the receiving records
/// in that receipt. Bought-parts effort PR3.
///
/// <para>Choice depends on what data the buyer trusts. Per-line value
/// (<see cref="ByExtendedValue"/>) works without weight data and is the
/// sensible default. <see cref="ByWeight"/> is better when freight is
/// genuinely volumetric (steel, bulky electronics) and the parts have
/// per-unit weight populated. <see cref="ByQuantity"/> is the edge case
/// where unit sizes are consistent and you'd rather split flat.
/// <see cref="Manual"/> punts the choice to the buyer entirely.</para>
///
/// <para>The vendor's preferred method is admin-configurable on
/// <c>Vendor.DefaultFreightAllocationMethod</c> (future PR). Today the
/// tenant default is <see cref="ByExtendedValue"/> and the buyer can
/// override per receipt.</para>
/// </summary>
public enum FreightAllocationMethod
{
    /// <summary>
    /// Default. Split freight in proportion to each line's extended value
    /// (qty × unit price). Doesn't require weight data; matches AP intuition.
    /// </summary>
    ByExtendedValue,

    /// <summary>
    /// Split freight in proportion to weight. Requires per-part weight
    /// populated; falls back to <see cref="ByExtendedValue"/> when missing.
    /// </summary>
    ByWeight,

    /// <summary>
    /// Split freight evenly per unit received. Useful when units are
    /// consistent in size and a flat per-unit allocation matches reality.
    /// </summary>
    ByQuantity,

    /// <summary>
    /// Buyer enters per-line freight directly. Used for unusual shipments
    /// where no auto-allocation captures the truth (one line is the whole
    /// pallet, the other is air-shipped, etc.).
    /// </summary>
    Manual,
}
