namespace Forge.Core.Models;

public record EarnedValueMetricsResponseModel
{
    public decimal BudgetedCostOfWorkScheduled { get; init; }
    public decimal BudgetedCostOfWorkPerformed { get; init; }
    public decimal ActualCostOfWorkPerformed { get; init; }
    public decimal ScheduleVariance { get; init; }
    public decimal CostVariance { get; init; }
    public decimal SchedulePerformanceIndex { get; init; }
    public decimal CostPerformanceIndex { get; init; }
    public decimal EstimateAtCompletion { get; init; }
    public decimal EstimateToComplete { get; init; }
}
