namespace Forge.Core.Models;

public record LotTraceShipmentModel(
    int ShipmentId,
    string ShipmentNumber,
    int CustomerId,
    string CustomerName,
    DateTimeOffset? ShippedDate,
    decimal Quantity);
