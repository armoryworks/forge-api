namespace Forge.Core.Models;

public record AtpBucket
{
    public DateOnly Date { get; init; }
    public decimal CumulativeSupply { get; init; }
    public decimal CumulativeDemand { get; init; }
    public decimal NetAvailable { get; init; }
}
