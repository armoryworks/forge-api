namespace Forge.Core.Models;

public record CreateConfiguratorRequestModel(
    string Name,
    string? Description,
    int BasePartId,
    decimal? BasePrice,
    string? ValidationRulesJson,
    string? PricingFormulaJson,
    List<CreateConfiguratorOptionRequestModel>? Options);
