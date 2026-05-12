namespace Forge.Core.Models;

public record ConfiguratorDetailResponseModel(
    int Id,
    string Name,
    string? Description,
    int BasePartId,
    string BasePartNumber,
    bool IsActive,
    decimal? BasePrice,
    string? ValidationRulesJson,
    string? PricingFormulaJson,
    List<ConfiguratorOptionResponseModel> Options,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
