namespace Forge.Core.Models;

public record ShipmentPackageResponseModel(
    int Id,
    int ShipmentId,
    string? TrackingNumber,
    string? Carrier,
    decimal? Weight,
    decimal? Length,
    decimal? Width,
    decimal? Height,
    string Status);
