namespace Forge.Core.Models;

public record CreatePlantRequestModel(
    string Code,
    string Name,
    int CompanyLocationId,
    string? TimeZone,
    string? CurrencyCode,
    bool IsDefault);
