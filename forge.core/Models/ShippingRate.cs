namespace Forge.Core.Models;

public record ShippingRate(
    string CarrierId,
    string CarrierName,
    string ServiceName,
    decimal Price,
    int EstimatedDays);
