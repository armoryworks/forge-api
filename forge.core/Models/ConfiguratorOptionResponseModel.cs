
namespace Forge.Core.Models;

public record ConfiguratorOptionResponseModel(
    int Id,
    string Name,
    ConfiguratorOptionType OptionType,
    string ValuesJson,
    string? PricingRuleJson,
    string? BomImpactJson,
    string? RoutingImpactJson,
    string? DependsOnOptionId,
    int SortOrder,
    bool IsRequired,
    string? HelpText,
    string? DefaultValue);
