using Forge.Core.Enums;

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

    /// <summary>Item (default) or Freight. A freight line has no part, never ships and posts to Freight Income.</summary>
    public DocumentLineType LineType { get; set; } = DocumentLineType.Item;
    public decimal ShippedQuantity { get; set; }
    public string? Notes { get; set; }
    public int? UomId { get; set; }

    /// <summary>Over-ship tolerance for this line, in percent. Null = the customer's, then the company default.</summary>
    public decimal? OverShipTolerancePct { get; set; }

    /// <summary>Under-ship tolerance for this line, in percent. Null = the customer's, then the company default.</summary>
    public decimal? UnderShipTolerancePct { get; set; }

    /// <summary>
    /// Quantity that will not ship because the line shipped inside its under-ship tolerance. It is no longer
    /// open demand or back order; it is recomputed whenever the line's shipments change.
    /// </summary>
    public decimal ShortClosedQuantity { get; set; }

    /// <summary>Why <see cref="ShortClosedQuantity"/> was set. <see cref="WithinToleranceReason"/> when a shipment inside the under-ship tolerance closed the line.</summary>
    public string? ShortCloseReason { get; set; }

    /// <summary>The <see cref="ShortCloseReason"/> recorded when shipping inside the under-ship tolerance closes the line.</summary>
    public const string WithinToleranceReason = "Within tolerance";

    public decimal LineTotal => Quantity * UnitPrice;
    public decimal RemainingQuantity => Quantity - ShippedQuantity - ShortClosedQuantity;
    public bool IsFullyShipped => ShippedQuantity + ShortClosedQuantity >= Quantity;

    /// <summary>True when the line was closed by shipping inside its under-ship tolerance.</summary>
    public bool ClosedWithinTolerance => ShortClosedQuantity > 0 && ShortCloseReason == WithinToleranceReason;

    public bool ShipsGoods => LineType != DocumentLineType.Freight && Part?.InventoryClass != InventoryClass.Service;

    public bool IsFreight => LineType == DocumentLineType.Freight;

    // F-032: per-line taxability code required by QBO AST. Null = inherit order-level default.
    // Values: "TAX" (taxable), "NON" (non-taxable). Propagated from Part.TaxCode at SO creation.
    public string? TaxCode { get; set; }

    public SalesOrder SalesOrder { get; set; } = null!;
    public Part? Part { get; set; }
    public UnitOfMeasure? Uom { get; set; }
    public ICollection<Job> Jobs { get; set; } = [];
    public ICollection<ShipmentLine> ShipmentLines { get; set; } = [];
}
