namespace Forge.Core.Models;

public record SalesOrderShipmentPackageModel(
    int Id,
    string? TrackingNumber,
    string? Carrier,
    decimal? Weight,
    decimal? Length,
    decimal? Width,
    decimal? Height,
    string? Status);
