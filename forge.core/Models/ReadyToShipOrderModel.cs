namespace Forge.Core.Models;

/// <summary>Shipping workspace: an open sales order with its unshipped lines — one row in the ready-to-ship queue.</summary>
public record ReadyToShipOrderModel(
    int SalesOrderId,
    string OrderNumber,
    int CustomerId,
    string CustomerName,
    int? ShippingAddressId,
    DateTimeOffset? RequestedDeliveryDate,
    string Status,
    List<ReadyToShipLineModel> Lines);
