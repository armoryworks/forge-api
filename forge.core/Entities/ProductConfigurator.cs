namespace Forge.Core.Entities;

public class ProductConfigurator : BaseAuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int BasePartId { get; set; }
    public bool IsActive { get; set; } = true;
    public string? ValidationRulesJson { get; set; }
    public decimal? BasePrice { get; set; }
    public string? PricingFormulaJson { get; set; }

    public Part BasePart { get; set; } = null!;
    public ICollection<ConfiguratorOption> Options { get; set; } = [];
    public ICollection<ProductConfiguration> Configurations { get; set; } = [];
}
