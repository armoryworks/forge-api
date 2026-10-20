namespace Forge.Core.Entities;

public class CycleCountLine : BaseEntity
{
    public int CycleCountId { get; set; }
    public int? BinContentId { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public int EntityId { get; set; }
    public decimal ExpectedQuantity { get; set; }
    public decimal ActualQuantity { get; set; }
    public decimal Variance => ActualQuantity - ExpectedQuantity;
    public string? Notes { get; set; }

    public CycleCount CycleCount { get; set; } = null!;
    public BinContent? BinContent { get; set; }
}
