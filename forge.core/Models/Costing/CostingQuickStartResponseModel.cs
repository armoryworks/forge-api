namespace Forge.Core.Models.Costing;

/// <summary>What applying a costing template configured, and the derived absorption rate.</summary>
public sealed record CostingQuickStartResponseModel(
    int CostingCostCenterId,
    int CostingPeriodId,
    IReadOnlyList<string> PoolsConfigured,
    decimal AnnualDirectLaborHours,
    decimal TotalAnnualOverhead,
    decimal OverheadRatePerLaborHour,
    bool GlBudgetsCreated,
    int LaborRatesSet,
    IReadOnlyList<string> Notes);
