namespace Forge.Core.Models;

public record SendOutRequestModel(
    decimal Quantity,
    decimal UnitCost,
    DateTimeOffset? ExpectedReturnDate,
    string? ShippingTrackingNumber,
    string? Notes,
    bool CreatePurchaseOrder = true);
