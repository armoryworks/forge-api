
namespace Forge.Core.Models;

public record CreateConfiguratorOptionRequestModel(
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
