namespace Forge.Core.Models;

/// <summary>
/// A single (SO line → quantity) allocation within a stage's line assignment.
/// </summary>
public record StageLineAllocationModel(
    int SalesOrderLineId,
    decimal Quantity);
