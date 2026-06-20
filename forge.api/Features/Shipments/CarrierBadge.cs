namespace Forge.Api.Features.Shipments;

/// <summary>
/// Maps a carrier (service id or display name) to a distinct color-coded badge so the carrier is
/// unmistakable at a glance on the ship document. Brand colors: FedEx purple/orange, UPS brown/gold,
/// USPS blue/red, DHL red/yellow. Unknown/manual carriers get a neutral slate badge.
/// </summary>
public static class CarrierBadge
{
    public static CarrierBadgeStyle For(string? carrierServiceIdOrName)
    {
        var key = (carrierServiceIdOrName ?? string.Empty).Trim().ToLowerInvariant();

        if (key.Contains("fedex")) return new CarrierBadgeStyle("FEDEX", "#4D148C", "#FF6600");
        if (key.Contains("ups")) return new CarrierBadgeStyle("UPS", "#351C15", "#FFB500");
        if (key.Contains("usps") || key.Contains("postal")) return new CarrierBadgeStyle("USPS", "#004B87", "#DA291C");
        if (key.Contains("dhl")) return new CarrierBadgeStyle("DHL", "#D40511", "#FFCC00");

        var label = string.IsNullOrWhiteSpace(carrierServiceIdOrName) ? "CARRIER" : carrierServiceIdOrName!.ToUpperInvariant();
        return new CarrierBadgeStyle(label, "#37474F", "#90A4AE");
    }
}
