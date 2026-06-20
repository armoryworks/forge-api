namespace Forge.Api.Features.Shipments;

/// <summary>
/// Where the raw carrier label PNG is stashed in object storage. The carrier returns the label only
/// once (at creation), so we keep it to (re)generate the combined ship document on demand.
/// </summary>
public static class ShipLabelStorage
{
    public const string Bucket = "forge-shipping-labels";

    public static string Key(int shipmentId) => $"{shipmentId}/carrier-label.png";
}
