namespace Forge.Core.Models;

/// <summary>Shipping workspace: an open sales-order line with quantity remaining to ship.</summary>
public record ReadyToShipLineModel(
    int SalesOrderLineId,
    int LineNumber,
    string Description,
    int? PartId,
    string? PartNumber,
    decimal Quantity,
    decimal ShippedQuantity,
    decimal RemainingQuantity);
