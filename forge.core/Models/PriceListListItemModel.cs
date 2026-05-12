namespace Forge.Core.Models;

public record PriceListListItemModel(
    int Id,
    string Name,
    string? Description,
    int? CustomerId,
    string? CustomerName,
    bool IsDefault,
    bool IsActive,
    int EntryCount,
    DateTimeOffset CreatedAt);
