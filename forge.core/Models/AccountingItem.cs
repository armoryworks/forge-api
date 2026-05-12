namespace Forge.Core.Models;

public record AccountingItem(
    string? ExternalId,
    string Name,
    string? Description,
    string? Type,
    decimal? UnitPrice,
    decimal? PurchaseCost,
    string? Sku,
    bool Active);
