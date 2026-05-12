namespace Forge.Core.Models;

/// <summary>
/// Pillar 3 — Read model for a single tiered-price row on a VendorPart.
/// Sorted client-side by MinQuantity ASC for tier-table display.
/// </summary>
public record VendorPartPriceTierResponseModel(
    int Id,
    int VendorPartId,
    decimal MinQuantity,
    decimal UnitPrice,
    string Currency,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    string? Notes);
