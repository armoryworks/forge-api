namespace Forge.Core.Models.Costing;

/// <summary>Outcome of freezing a costing period: how many budgets were rated and how many
/// work-center rate rows were composed.</summary>
public record FreezeCostingPeriodResultModel(
    int PeriodId,
    int BudgetsRated,
    int WorkCentersRated);
