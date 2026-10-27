namespace Forge.Core.Entities;

/// <summary>
/// One held bin row going back on a <see cref="VendorReturn"/>. <see cref="UnitPrice"/> is the purchase price in the
/// return's currency; <see cref="UnitCost"/> is the functional cost the goods are relieved at (price × receipt rate).
/// </summary>
public class VendorReturnLine : BaseEntity
{
    public int VendorReturnId { get; set; }

    public int LineNumber { get; set; }

    public int PartId { get; set; }

    public string? LotNumber { get; set; }

    public int? LocationId { get; set; }

    public int? BinContentId { get; set; }

    public decimal Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal UnitCost { get; set; }

    /// <summary>The functional value shipping relieved from inventory into the clearing account; null until it ships.</summary>
    public decimal? ShippedCost { get; set; }

    public int? BinMovementId { get; set; }

    public VendorReturn VendorReturn { get; set; } = null!;
    public Part Part { get; set; } = null!;
    public StorageLocation? Location { get; set; }
}
