namespace Forge.Api.Features.Accounting.StockLoads;

/// <summary>
/// One opening-stock row as the caller sends it: the part by id or number, the location by id or by barcode / name
/// (blank = the default location), an optional lot and expiry, the quantity and the cutover unit cost.
/// </summary>
public sealed record StockLoadLineInput(
    string? PartNumber,
    string? Location,
    string? LotNumber,
    DateOnly? ExpirationDate,
    decimal Quantity,
    decimal UnitCost,
    int? PartId = null,
    int? StorageLocationId = null);
