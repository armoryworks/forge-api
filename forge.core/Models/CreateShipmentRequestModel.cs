namespace Forge.Core.Models;

public record CreateShipmentRequestModel(
    int SalesOrderId,
    int? ShippingAddressId,
    string? Carrier,
    string? TrackingNumber,
    decimal? ShippingCost,
    decimal? Weight,
    string? Notes,
    List<CreateShipmentLineModel> Lines);

public record CreateShipmentLineModel(
    int? SalesOrderLineId,
    decimal Quantity,
    string? Notes,
    int? PartId = null);
