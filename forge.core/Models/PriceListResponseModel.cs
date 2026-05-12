namespace Forge.Core.Models;

public record PriceListResponseModel(
    int Id,
    string Name,
    string? Description,
    int? CustomerId,
    string? CustomerName,
    bool IsDefault,
    bool IsActive,
    DateTimeOffset? EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    List<PriceListEntryResponseModel> Entries,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
