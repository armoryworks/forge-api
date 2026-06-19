using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class Shipment : BaseAuditableEntity, IConcurrencyVersioned
{
    /// <summary>Optimistic-locking version. See IConcurrencyVersioned. WU-11.</summary>
    public uint Version { get; set; }

    public string ShipmentNumber { get; set; } = string.Empty;
    public int SalesOrderId { get; set; }
    public int? ShippingAddressId { get; set; }
    public ShipmentStatus Status { get; set; } = ShipmentStatus.Pending;
    public string? Carrier { get; set; }

    /// <summary>
    /// The selected <see cref="Entities.Carrier"/> (master data) when one is assigned. Optional and
    /// additive: the legacy free-text <see cref="Carrier"/> string above stays for shipments created
    /// without a carrier record. Drives the scan-to-ship gate and delivery automation.
    /// </summary>
    public int? CarrierId { get; set; }

    public string? TrackingNumber { get; set; }

    /// <summary>
    /// Forge-issued, coverage-bound scan token rendered as the master QR on the shipment's label
    /// wrapper. Format <c>v1.{shipmentNumber}.{coverageHash}</c>, where coverageHash binds the exact
    /// (salesOrderLineId, quantity) set this shipment covers — so a reprint after the coverage
    /// changes invalidates a stale scan. The scan-to-ship gate compares the scanned value to this.
    /// </summary>
    public string? ScanCode { get; set; }
    public DateTimeOffset? ShippedDate { get; set; }
    public DateTimeOffset? DeliveredDate { get; set; }
    public decimal? ShippingCost { get; set; }
    public decimal? Weight { get; set; }
    public string? Notes { get; set; }
    public string? ServiceType { get; set; }
    public DateTimeOffset? EstimatedDeliveryDate { get; set; }
    public string? FreightClass { get; set; }
    public decimal? InsuredValue { get; set; }
    public bool SignatureRequired { get; set; }
    public string? BillOfLadingNumber { get; set; }

    /// <summary>
    /// Carrier pickup confirmation (PRP / confirmation code) once a courier pickup is scheduled for this
    /// shipment; null when none is scheduled. Per-shipment pickup model — a shipment has at most one.
    /// </summary>
    public string? PickupConfirmationNumber { get; set; }
    public DateTimeOffset? PickupScheduledDate { get; set; }

    public SalesOrder SalesOrder { get; set; } = null!;
    public Carrier? AssignedCarrier { get; set; }
    public CustomerAddress? ShippingAddress { get; set; }
    public ICollection<ShipmentLine> Lines { get; set; } = [];
    public ICollection<ShipmentPackage> Packages { get; set; } = [];
    public Invoice? Invoice { get; set; }
}
