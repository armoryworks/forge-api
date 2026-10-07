namespace Forge.Core.Entities;

/// <summary>
/// A shipment (and the customer who received it) that carried an affected lot, resolved from the
/// Ship bin movements stamped with that lot; the quantity is net of reversals. Ship movements that
/// carried no lot, and shipment lines that never relieved inventory, fall back to the lot's
/// job → sales-order-line chain, and those rows are approximate.
/// </summary>
public class RecallAffectedShipment : BaseAuditableEntity
{
    public int RecallId { get; set; }
    public Recall Recall { get; set; } = null!;

    public int ShipmentId { get; set; }
    public Shipment Shipment { get; set; } = null!;

    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    public decimal AffectedQuantity { get; set; }
    public DateTimeOffset? ShippedDate { get; set; }
    public string? TrackingNumber { get; set; }
    public bool IsApproximate { get; set; }
}
