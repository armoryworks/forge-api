namespace Forge.Core.Models;

public record PhysicalInventoryVarianceResponseModel
{
    public int PhysicalInventoryId { get; init; }
    public string InventoryNumber { get; init; } = string.Empty;
    public IReadOnlyList<PhysicalInventoryVarianceLineResponseModel> Lines { get; init; } = [];
    public int TagCount { get; init; }
    public int VarianceTagCount { get; init; }
    public decimal ShortageValue { get; init; }
    public decimal OverageValue { get; init; }
    public decimal NetVarianceValue { get; init; }
}
