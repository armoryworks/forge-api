namespace Forge.Core.Models;

public record ConfirmDropShipDeliveryRequestModel
{
    public decimal DeliveredQuantity { get; init; }
    public string? TrackingNumber { get; init; }
}
