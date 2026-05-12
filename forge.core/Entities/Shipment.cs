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
    public string? TrackingNumber { get; set; }
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

    public SalesOrder SalesOrder { get; set; } = null!;
    public CustomerAddress? ShippingAddress { get; set; }
    public ICollection<ShipmentLine> Lines { get; set; } = [];
    public ICollection<ShipmentPackage> Packages { get; set; } = [];
    public Invoice? Invoice { get; set; }
}
