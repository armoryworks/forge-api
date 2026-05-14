
namespace Forge.Core.Entities;

public class ConfiguratorOption : BaseEntity
{
    public int ConfiguratorId { get; set; }
    public string Name { get; set; } = string.Empty;
    public ConfiguratorOptionType OptionType { get; set; }
    public string ValuesJson { get; set; } = "[]";
    public string? PricingRuleJson { get; set; }
    public string? BomImpactJson { get; set; }
    public string? RoutingImpactJson { get; set; }
    public string? DependsOnOptionId { get; set; }
    public int SortOrder { get; set; }
    public bool IsRequired { get; set; }
    public string? HelpText { get; set; }
    public string? DefaultValue { get; set; }

    public ProductConfigurator Configurator { get; set; } = null!;
}
