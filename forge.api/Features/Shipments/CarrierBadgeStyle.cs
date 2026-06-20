namespace Forge.Api.Features.Shipments;

/// <summary>Color-coded + symbolic identity for a carrier, used on the ship document's carrier badge.</summary>
public record CarrierBadgeStyle(string Label, string Primary, string Accent);
