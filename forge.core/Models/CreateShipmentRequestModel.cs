namespace Forge.Core.Models;

public record CreateShipmentRequestModel(
    int SalesOrderId,
    int? ShippingAddressId,
    string? Carrier,
    string? TrackingNumber,
    decimal? ShippingCost,
    decimal? Weight,
    string? Notes,
    List<CreateShipmentLineModel> Lines,
    int? CarrierId = null,
    // Optional caller-supplied shipment number — gated by shipments.allow_manual_numbers.
    string? ShipmentNumber = null);

public record CreateShipmentLineModel(
    int? SalesOrderLineId,
    decimal Quantity,
    string? Notes,
    int? PartId = null);
