using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class ProductConfiguration : BaseAuditableEntity
{
    public int ConfiguratorId { get; set; }
    public string ConfigurationCode { get; set; } = string.Empty;
    public string SelectionsJson { get; set; } = "{}";
    public decimal ComputedPrice { get; set; }
    public string? GeneratedBomJson { get; set; }
    public string? GeneratedRoutingJson { get; set; }
    public int? QuoteId { get; set; }
    public int? PartId { get; set; }
    public ConfigurationStatus Status { get; set; } = ConfigurationStatus.Draft;

    public ProductConfigurator Configurator { get; set; } = null!;
    public Quote? Quote { get; set; }
    public Part? Part { get; set; }
}
