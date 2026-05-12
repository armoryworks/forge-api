namespace Forge.Core.Models;

public record NcrCostSummary
{
    public decimal MaterialCost { get; init; }
    public decimal LaborCost { get; init; }
    public decimal TotalCost { get; init; }
    public int AffectedQuantity { get; init; }
    public decimal CostPerUnit { get; init; }
}
