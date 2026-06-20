namespace Forge.Core.Models;

/// <summary>
/// A carrier shipping label. <see cref="LabelUrl"/> is the carrier's hosted label (when the carrier
/// returns a URL); <see cref="LabelBytes"/> is the raw label image (PNG) when the carrier returns it
/// inline — used to compose the combined ship document.
/// </summary>
public record ShippingLabel(
    string TrackingNumber,
    string LabelUrl,
    string CarrierName,
    byte[]? LabelBytes = null);
