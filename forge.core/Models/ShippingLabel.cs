namespace Forge.Core.Models;

public record ShippingLabel(
    string TrackingNumber,
    string LabelUrl,
    string CarrierName);
