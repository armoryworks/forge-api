namespace Forge.Core.Models;

/// <summary>
/// One SO-line quantity allocation to a stage. PartNumber/Description are copied
/// from the underlying <c>SalesOrderLine</c> for display.
/// </summary>
public record SalesOrderStageLineResponseModel(
    int Id,
    int SalesOrderLineId,
    string? PartNumber,
    string Description,
    decimal Quantity);
