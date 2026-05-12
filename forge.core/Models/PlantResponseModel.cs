namespace Forge.Core.Models;

public record PlantResponseModel(
    int Id,
    string Code,
    string Name,
    int CompanyLocationId,
    string LocationName,
    string? TimeZone,
    string? CurrencyCode,
    bool IsActive,
    bool IsDefault,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
