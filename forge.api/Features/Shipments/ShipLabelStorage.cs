namespace Forge.Api.Features.Shipments;

/// <summary>
/// Where the raw carrier label PNG is stashed in object storage. The carrier returns the label only
/// once (at creation), so we keep it to (re)generate the combined ship document on demand.
/// </summary>
public static class ShipLabelStorage
{
    public const string Bucket = "forge-shipping-labels";

    public static string Key(int shipmentId) => $"{shipmentId}/carrier-label.png";

    /// <summary>
    /// True when the bytes start with the PNG signature. Only a PNG label can be wrapped into the ship
    /// document; carriers that return a PDF or ZPL label are not stored under the PNG key.
    /// </summary>
    public static bool IsPng(ReadOnlySpan<byte> bytes)
        => bytes.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
}
