namespace Forge.Core.Models;

public record UpdatePlantRequestModel(
    string Code,
    string Name,
    int CompanyLocationId,
    string? TimeZone,
    string? CurrencyCode,
    bool IsActive,
    bool IsDefault);
