namespace Forge.Core.Enums;

/// <summary>How a costing-template line's answer converts to an annual budget amount.</summary>
public enum CostingAmountBasis
{
    /// <summary>The value is the annual amount.</summary>
    AnnualAmount,
    /// <summary>The value is a monthly amount — annualized ×12.</summary>
    MonthlyAmount,
    /// <summary>The value is per employee per month — annualized ×12 × direct headcount.</summary>
    MonthlyPerEmployee,
    /// <summary>The value is a percent of annual direct wages (headcount × 2080 × wage).</summary>
    PercentOfWages,
}
