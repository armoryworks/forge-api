namespace Forge.Core.Models;

public record UpdateShipmentRequestModel(
    string? Carrier,
    string? TrackingNumber,
    decimal? ShippingCost,
    decimal? Weight,
    string? Notes,
    int? ShippingAddressId = null,
    decimal? Length = null,
    decimal? Width = null,
    decimal? Height = null);
