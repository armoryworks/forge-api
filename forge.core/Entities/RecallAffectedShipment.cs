namespace Forge.Core.Entities;

/// <summary>
/// A shipment (and the customer who received it) that carried an affected lot, resolved via
/// the lot's job → sales-order-line → shipment-line chain. Granularity is the sales-order
/// line, not the specific lot — ShipmentLine has no lot linkage — so this identifies the
/// customer/date/tracking to notify; the exact affected sub-quantity is a best estimate.
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
}
