using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Forge.Core.Entities.Costing;

/// <summary>A pool's budget for one costing period: the budgeted spend and budgeted driver quantity,
/// whose quotient is the frozen absorption rate. Overstating the driver quantity is the classic way a
/// shop under-recovers overhead, so the UI shows prior-period actuals beside it.</summary>
public class OverheadPoolBudget : BaseAuditableEntity
{
    /// <summary>The pool being budgeted.</summary>
    public int OverheadCostPoolId { get; set; }

    /// <summary>The costing period this budget applies to.</summary>
    public int CostingPeriodId { get; set; }

    /// <summary>Budgeted overhead spend for the period.</summary>
    public decimal BudgetAmount { get; set; }

    /// <summary>Budgeted driver quantity (e.g. planned machine hours) for the period.</summary>
    public decimal BudgetDriverQty { get; set; }

    /// <summary>Derived absorption rate = <see cref="BudgetAmount"/> / <see cref="BudgetDriverQty"/>,
    /// computed and frozen at period freeze.</summary>
    public decimal DerivedRate { get; set; }

    /// <summary>The pool being budgeted.</summary>
    [ForeignKey(nameof(OverheadCostPoolId))]
    public OverheadCostPool? OverheadCostPool { get; set; }

    /// <summary>The costing period.</summary>
    [ForeignKey(nameof(CostingPeriodId))]
    public CostingPeriod? CostingPeriod { get; set; }
}
