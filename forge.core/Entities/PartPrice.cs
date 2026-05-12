namespace Forge.Core.Entities;

/// <summary>
/// Part-level effective-dated default sales price. One row per
/// (part, effective period). Used as rung 2 of IPartPricingResolver:
/// - <see cref="UnitPrice"/> applies when no customer-scoped PriceListEntry hits.
/// - When multiple rows are active simultaneously, the latest EffectiveFrom wins.
/// - History rows (EffectiveTo set) drive the price-history surfaces on Part detail.
/// - Notes capture the rationale for a price change ("competitor matched", "raw cost up", etc.)
///
/// NOT a cost record. Vendor cost tiers live on <see cref="VendorPartPriceTier"/>.
/// Customer-tiered sales pricing lives on <see cref="PriceListEntry"/>.
/// </summary>
public class PartPrice : BaseEntity
{
    public int PartId { get; set; }
    public decimal UnitPrice { get; set; }

    /// <summary>
    /// ISO-4217 currency code (e.g. "USD", "EUR"). Defaults to "USD" so existing
    /// rows backfilled by the migration retain a sensible value. The
    /// IPartPricingResolver echoes this back through ResolvedPartPrice.Currency
    /// so the UI can decide whether to suffix the code.
    /// </summary>
    public string Currency { get; set; } = "USD";

    public DateTimeOffset EffectiveFrom { get; set; }
    public DateTimeOffset? EffectiveTo { get; set; }
    public string? Notes { get; set; }

    /// <summary>
    /// Wall-clock instant the row was inserted. Distinct from
    /// <see cref="EffectiveFrom"/> — admins may post a row dated in the
    /// past or future, so EffectiveFrom and CreatedAt routinely differ.
    /// Set explicitly by handlers (PartPrice extends BaseEntity, not
    /// BaseAuditableEntity, so the auto-timestamp pipeline doesn't touch it).
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    public Part Part { get; set; } = null!;
}
