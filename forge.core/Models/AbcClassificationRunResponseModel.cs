namespace Forge.Core.Models;

public record AbcClassificationRunResponseModel
{
    public int Id { get; init; }
    public DateTimeOffset RunDate { get; init; }
    public int TotalParts { get; init; }
    public int ClassACount { get; init; }
    public int ClassBCount { get; init; }
    public int ClassCCount { get; init; }
    public decimal ClassAThresholdPercent { get; init; }
    public decimal ClassBThresholdPercent { get; init; }
    public decimal TotalAnnualUsageValue { get; init; }
    public int LookbackMonths { get; init; }
}
