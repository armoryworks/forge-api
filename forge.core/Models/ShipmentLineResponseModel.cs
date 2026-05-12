namespace Forge.Core.Models;

public record ShipmentLineResponseModel(
    int Id,
    int? SalesOrderLineId,
    int? PartId,
    string Description,
    decimal Quantity,
    string? Notes);
