namespace Forge.Core.Entities;

public class CycleCountLine : BaseEntity
{
    public int CycleCountId { get; set; }
    public int? BinContentId { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public int EntityId { get; set; }
    public int ExpectedQuantity { get; set; }
    public int ActualQuantity { get; set; }
    public int Variance => ActualQuantity - ExpectedQuantity;
    public string? Notes { get; set; }

    public CycleCount CycleCount { get; set; } = null!;
    public BinContent? BinContent { get; set; }
}
