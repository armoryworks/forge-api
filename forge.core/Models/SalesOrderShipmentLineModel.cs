namespace Forge.Core.Models;

public record SalesOrderShipmentLineModel(
    int Id,
    int? PartId,
    string? PartNumber,
    decimal Quantity,
    string? Notes,
    int? SalesOrderLineId);
