namespace Forge.Core.Entities;

public class SalesOrderLine : BaseEntity
{
    public int SalesOrderId { get; set; }
    public int? PartId { get; set; }
    public string Description { get; set; } = string.Empty;
    // Phase 3 / WU-10 / F8-partial: quantities are decimal, not int. UoM-aware
    // shops need fractional quantities — material-by-weight (lb, kg), by-time
    // (hr), by-volume (gal, l). decimal(18, 4). Cases EDGE-DECIMAL-PRECISION-001
    // / -004.
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public int LineNumber { get; set; }
    public decimal ShippedQuantity { get; set; }
    public string? Notes { get; set; }
    public int? UomId { get; set; }

    public decimal LineTotal => Quantity * UnitPrice;
    public decimal RemainingQuantity => Quantity - ShippedQuantity;
    public bool IsFullyShipped => ShippedQuantity >= Quantity;

    // F-032: per-line taxability code required by QBO AST. Null = inherit order-level default.
    // Values: "TAX" (taxable), "NON" (non-taxable). Propagated from Part.TaxCode at SO creation.
    public string? TaxCode { get; set; }

    public SalesOrder SalesOrder { get; set; } = null!;
    public Part? Part { get; set; }
    public UnitOfMeasure? Uom { get; set; }
    public ICollection<Job> Jobs { get; set; } = [];
    public ICollection<ShipmentLine> ShipmentLines { get; set; } = [];
}
