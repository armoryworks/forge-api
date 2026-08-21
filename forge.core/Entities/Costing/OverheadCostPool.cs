using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

using Forge.Core.Enums;

namespace Forge.Core.Entities.Costing;

/// <summary>An overhead pool: a bucket of indirect cost that accumulates actuals and is absorbed into
/// product cost at a rate per unit of its driver. Lives on a cost center (and optionally targets one
/// work center). Overhead lives here and on work centers — never on a BOM line.</summary>
public class OverheadCostPool : BaseAuditableEntity
{
    /// <summary>Owning costing cost center.</summary>
    public int CostingCostCenterId { get; set; }

    /// <summary>Optional target work center — when set, this pool's rate feeds that work center only;
    /// otherwise it applies across the cost center by its driver.</summary>
    public int? WorkCenterId { get; set; }

    /// <summary>Short unique code.</summary>
    [MaxLength(32)]
    public string Code { get; set; } = string.Empty;

    /// <summary>Human-readable name.</summary>
    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Cost behavior — fixed, variable, or semi (split via <see cref="FixedPortion"/>).</summary>
    public OverheadBehavior Behavior { get; set; }

    /// <summary>For a semi-variable pool, the fixed fraction (0..1) of its budget.</summary>
    public decimal? FixedPortion { get; set; }

    /// <summary>The activity that absorbs this pool (machine hour, labor hour, material dollar, …).</summary>
    public OverheadDriver Driver { get; set; }

    /// <summary>Owning costing cost center.</summary>
    [ForeignKey(nameof(CostingCostCenterId))]
    public CostingCostCenter? CostingCostCenter { get; set; }

    /// <summary>Optional target work center.</summary>
    [ForeignKey(nameof(WorkCenterId))]
    public WorkCenter? WorkCenter { get; set; }
}
