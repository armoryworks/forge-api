namespace Forge.Core.Models;

public record AbcClassificationParametersModel
{
    public decimal ClassAThresholdPercent { get; init; } = 80m;
    public decimal ClassBThresholdPercent { get; init; } = 95m;
    public int LookbackMonths { get; init; } = 12;
    public int ClassACycleCountDays { get; init; } = 30;
    public int ClassBCycleCountDays { get; init; } = 90;
    public int ClassCCycleCountDays { get; init; } = 365;
}
