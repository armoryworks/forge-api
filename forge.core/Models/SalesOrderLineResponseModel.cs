namespace Forge.Core.Models;

// Phase 3 / WU-10 / F8-partial — quantities are decimal (was int).
public record SalesOrderLineResponseModel(
    int Id,
    int? PartId,
    string? PartNumber,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal LineTotal,
    int LineNumber,
    decimal ShippedQuantity,
    decimal RemainingQuantity,
    bool IsFullyShipped,
    string? Notes,
    List<SalesOrderLineJobModel> Jobs);
