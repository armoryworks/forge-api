namespace Forge.Core.Models;

public record ConfiguratorResponseModel(
    int Id,
    string Name,
    string? Description,
    int BasePartId,
    string BasePartNumber,
    bool IsActive,
    decimal? BasePrice,
    int OptionCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
