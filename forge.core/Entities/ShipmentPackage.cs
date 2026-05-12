namespace Forge.Core.Entities;

public class ShipmentPackage : BaseEntity
{
    public int ShipmentId { get; set; }
    public string? TrackingNumber { get; set; }
    public string? Carrier { get; set; }
    public decimal? Weight { get; set; }
    public decimal? Length { get; set; }
    public decimal? Width { get; set; }
    public decimal? Height { get; set; }
    public string Status { get; set; } = "Pending";

    public Shipment Shipment { get; set; } = null!;
}
