namespace Forge.Core.Models;

public record UpdateConfiguratorRequestModel(
    string Name,
    string? Description,
    int BasePartId,
    decimal? BasePrice,
    bool IsActive,
    string? ValidationRulesJson,
    string? PricingFormulaJson,
    List<CreateConfiguratorOptionRequestModel>? Options);
