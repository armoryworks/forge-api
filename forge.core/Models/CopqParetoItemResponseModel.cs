namespace Forge.Core.Models;

public record CopqParetoItemResponseModel
{
    public string DefectType { get; init; } = string.Empty;
    public decimal Cost { get; init; }
    public int Occurrences { get; init; }
    public decimal CumulativePercent { get; init; }
}
