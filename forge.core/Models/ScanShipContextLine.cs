namespace Forge.Core.Models;

public record ScanShipContextLine(
    int ShipmentLineId,
    int ShipmentId,
    string ShipmentNumber,
    string? SalesOrderNumber,
    decimal RequiredQuantity);
