namespace Forge.Core.Models;

public record SalesOrderShipmentModel(
    int Id,
    string ShipmentNumber,
    string Status,
    string? Carrier,
    string? TrackingNumber,
    DateTimeOffset? ShippedDate,
    DateTimeOffset? DeliveredDate,
    decimal ShippingCost,
    decimal? Weight,
    string? Notes,
    List<SalesOrderShipmentLineModel> Lines,
    List<SalesOrderShipmentPackageModel> Packages);
