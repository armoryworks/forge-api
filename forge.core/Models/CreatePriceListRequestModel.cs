namespace Forge.Core.Models;

/// <summary>
/// Body for <c>POST /api/v1/price-lists</c>.
///
/// <see cref="Entries"/> is optional — the customer-detail Pricing tab UI
/// creates an empty list first (parent CRUD dialog) and then adds entries
/// row-by-row through the entries dialog. Bulk import / seed-style flows
/// can still pass an initial entry batch.
///
/// <see cref="IsActive"/> mirrors the entity column; existing call sites that
/// omit it default to <c>true</c> so seed and back-compat code keep working.
/// </summary>
public record CreatePriceListRequestModel(
    string Name,
    string? Description,
    int? CustomerId,
    bool IsDefault,
    DateTimeOffset? EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    List<CreatePriceListEntryModel>? Entries = null,
    bool IsActive = true);

public record CreatePriceListEntryModel(
    int PartId,
    decimal UnitPrice,
    int MinQuantity);
