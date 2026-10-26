namespace Forge.Api.Features.Accounting.StockLoads;

/// <summary>One stock-load row for the screen.</summary>
public sealed record StockLoadLineModel(
    int Id,
    int LineNumber,
    int PartId,
    string PartNumber,
    string? PartDescription,
    int StorageLocationId,
    string LocationName,
    string? LotNumber,
    DateOnly? ExpirationDate,
    decimal Quantity,
    decimal UnitCost,
    decimal ExtendedValue);
