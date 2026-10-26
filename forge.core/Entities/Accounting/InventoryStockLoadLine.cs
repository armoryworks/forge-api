namespace Forge.Core.Entities.Accounting;

/// <summary>One opening-stock row: a part at a location (and lot), with its quantity and cutover unit cost.</summary>
public class InventoryStockLoadLine : BaseEntity
{
    public int StockLoadId { get; set; }

    public int LineNumber { get; set; }

    public int PartId { get; set; }

    public int StorageLocationId { get; set; }

    public string? LotNumber { get; set; }

    public DateOnly? ExpirationDate { get; set; }

    public decimal Quantity { get; set; }

    public decimal UnitCost { get; set; }

    /// <summary>Quantity × unit cost, rounded to the cent.</summary>
    public decimal ExtendedValue { get; set; }

    /// <summary>The bin content the post created.</summary>
    public int? BinContentId { get; set; }

    /// <summary>The audit movement the post wrote.</summary>
    public int? BinMovementId { get; set; }

    public InventoryStockLoad StockLoad { get; set; } = null!;
    public Part Part { get; set; } = null!;
    public StorageLocation StorageLocation { get; set; } = null!;
}
