namespace Forge.Core.Models.Costing;

/// <summary>A pool's budget for a period and the derived absorption rate.</summary>
public record OverheadPoolBudgetResponseModel(
    int Id,
    int OverheadCostPoolId,
    int CostingPeriodId,
    decimal BudgetAmount,
    decimal BudgetDriverQty,
    decimal DerivedRate);
