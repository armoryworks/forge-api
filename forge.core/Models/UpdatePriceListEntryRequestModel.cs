namespace Forge.Core.Models;

/// <summary>
/// Request body for <c>PUT /api/v1/price-list-entries/{id}</c>.
/// PartId is intentionally absent — entries are re-keyed via delete + recreate
/// when the user wants to assign a different part to a tier.
/// </summary>
public record UpdatePriceListEntryRequestModel(
    decimal UnitPrice,
    int MinQuantity,
    string Currency,
    string? Notes);
