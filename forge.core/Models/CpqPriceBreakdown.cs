namespace Forge.Core.Models;

public record CpqPriceBreakdown
{
    public string OptionName { get; init; } = "";
    public string Selection { get; init; } = "";
    public decimal PriceImpact { get; init; }
}
