namespace Forge.Core.Models;

public record ShipmentListItemModel(
    int Id,
    string ShipmentNumber,
    int SalesOrderId,
    string SalesOrderNumber,
    string CustomerName,
    string Status,
    string? Carrier,
    string? TrackingNumber,
    DateTimeOffset? ShippedDate,
    DateTimeOffset CreatedAt);
