namespace Forge.Core.Entities;

/// <summary>
/// UoM purchase-units effort — a purchasable size/form of a Part
/// (e.g. "4×8 sheet" = 32 sqft, "1 kg bar" = 1000 g, "bag of 100" = 100 ea).
///
/// <para>Part-level because the content quantity is <b>part-intrinsic</b>: a 4×8 sheet is 32 sqft
/// no matter who sells it. Defining options once on the Part avoids vendors disagreeing on a
/// form's size and lets pricing compare vendors for the <i>same</i> option. Vendors price the
/// options they carry via <see cref="VendorPartPriceTier.PurchaseUnitId"/>; a tier with a null
/// option prices the part per single base unit (the legacy single-option default).</para>
///
/// <para><see cref="ContentQuantity"/> is expressed in <see cref="ContentUom"/>, which must share
/// a <c>UomCategory</c> with the part's <c>StockUom</c> (area↔area, mass↔mass, …). Cost derives as
/// <c>tierPrice ÷ ContentQuantity</c> → per base/stock unit.</para>
///
/// <para>Two options may share the same content + UoM but differ in shape (32 sqft as a 4×8 sheet
/// vs. a 64 ft × 6 in roll) — they coexist as distinct rows distinguished by <see cref="Label"/>.
/// Physical dimensions (for fit/yield) are deferred; label disambiguates for v1.</para>
/// </summary>
public class PartPurchaseUnit : BaseAuditableEntity
{
    public int PartId { get; set; }

    /// <summary>Vendor-agnostic label, e.g. "4×8 sheet", "1 kg bar", "bag of 100".</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>How much of the part's base/stock UoM one of this option contains (8 sqft, 100 ea).</summary>
    public decimal ContentQuantity { get; set; }

    /// <summary>UoM the <see cref="ContentQuantity"/> is in — must be compatible with the part's StockUom.</summary>
    public int? ContentUomId { get; set; }

    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public Part Part { get; set; } = null!;
    public UnitOfMeasure? ContentUom { get; set; }
}
