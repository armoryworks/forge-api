namespace Forge.Core.Models;

public record ShipmentRequest(
    ShippingAddress FromAddress,
    ShippingAddress ToAddress,
    List<ShippingPackage> Packages,
    string? ServiceType);
