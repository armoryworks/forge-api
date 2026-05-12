namespace Forge.Core.Models;

/// <summary>
/// Request body for <c>POST /api/v1/price-lists/{id}/entries</c>.
/// PartId is required on create. To reassign an existing entry to a different
/// part, delete it and recreate — the entry id is keyed off (PriceListId,
/// PartId, MinQuantity).
/// </summary>
public record CreatePriceListEntryRequestModel(
    int PartId,
    decimal UnitPrice,
    int MinQuantity,
    string Currency,
    string? Notes);
