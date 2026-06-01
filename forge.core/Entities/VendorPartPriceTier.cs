namespace Forge.Core.Entities;

/// <summary>
/// Pillar 3 — Tiered pricing per VendorPart. A vendor typically quotes
/// "$5/each at qty 1-99, $4.50 at qty 100-499, $4.00 at qty 500+" — each
/// tier is a row keyed by min-quantity. Price-lookup picks the tier whose
/// MinQuantity is the largest value &lt;= the requested quantity.
///
/// Effective-dated so vendors can quote forward-pricing (next quarter's
/// rates) without overwriting current pricing. EffectiveTo nullable for
/// open-ended pricing.
/// </summary>
public class VendorPartPriceTier : BaseEntity
{
    public int VendorPartId { get; set; }

    /// <summary>
    /// UoM purchase-units effort — which <see cref="PartPurchaseUnit"/> (size/form) this tier
    /// prices. Null = the part's single default option (priced per base unit, legacy behavior).
    /// When the part has multiple options, every tier carries one; per-base-unit cost =
    /// <see cref="UnitPrice"/> ÷ option.ContentQuantity.
    /// </summary>
    public int? PurchaseUnitId { get; set; }

    public decimal MinQuantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }

    /// <summary>ISO-4217 currency code (e.g., 'USD', 'EUR'). Defaults to vendor's currency.</summary>
    public string Currency { get; set; } = "USD";

    public DateTimeOffset EffectiveFrom { get; set; }
    public DateTimeOffset? EffectiveTo { get; set; }

    public string? Notes { get; set; }

    /// <summary>
    /// True when the vendor's quoted price already includes freight (i.e.,
    /// "door-to-door" pricing). Bought-parts effort PR2. When true, the
    /// landed-cost calculation skips the freight component for this tier
    /// and the cost tab renders freight as "—" (not $0) so the distinction
    /// is visible. Lives on the tier (not the VendorPart) so a vendor can
    /// have free shipping above qty N but not below.
    /// </summary>
    public bool FreightIncluded { get; set; }

    public VendorPart VendorPart { get; set; } = null!;
    public PartPurchaseUnit? PurchaseUnit { get; set; }
}
