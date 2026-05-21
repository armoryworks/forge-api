namespace Forge.Core.Entities;

public class ShipmentLine : BaseEntity
{
    public int ShipmentId { get; set; }
    public int? SalesOrderLineId { get; set; }
    public int? PartId { get; set; }
    // Phase 3 / WU-23 (F8-broad): UoM-aware fractional quantities. Promoted to
    // decimal(18,4) so a fractional SO line can ship without truncation.
    public decimal Quantity { get; set; }
    public string? Notes { get; set; }
    public string? Description { get; set; }
    public decimal? Weight { get; set; }
    public decimal? Length { get; set; }
    public decimal? Width { get; set; }
    public decimal? Height { get; set; }
    public bool IsHazmat { get; set; }
    public string? HandlingInstructions { get; set; }
    public string? SerialNumbers { get; set; }
    // BE-1 / F-030: set exactly once when inventory is relieved for this line (idempotency guard).
    public DateTimeOffset? InventoryRelievedAt { get; set; }

    public Shipment Shipment { get; set; } = null!;
    public SalesOrderLine? SalesOrderLine { get; set; }
    public Part? Part { get; set; }
}
