namespace Forge.Api.Features.Shipments;

/// <summary>
/// Normalizes the free-text carrier tracking status (IShippingService returns a provider-specific
/// <c>Status</c> string) into the one signal the delivery automation acts on: is it delivered?
/// Provider-agnostic on purpose — EasyPost's "delivered", a direct carrier's "Delivery Completed",
/// and similar all collapse to the same answer, so the poll job and webhook ingest share one rule.
/// </summary>
public static class ShipmentDeliveryStatus
{
    public static bool IsDelivered(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return false;
        var s = status.Trim().ToLowerInvariant();
        // "delivered" covers "Delivered" / "Package delivered" / "Delivery completed"; "completed" is
        // the other common terminal phrasing. Substring match tolerates carrier-specific prefixes.
        return s.Contains("delivered") || s == "completed";
    }
}
