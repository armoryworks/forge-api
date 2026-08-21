using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Forge.Core.Entities.Costing;

/// <summary>Frozen per-period cost rates for a work center, composed at freeze from the overhead pools
/// that feed it. The cost roll reads these; nothing recomputes them mid-period.</summary>
public class WorkCenterCostRate : BaseAuditableEntity
{
    /// <summary>The work center these rates apply to (reuses the existing <see cref="Entities.WorkCenter"/>).</summary>
    public int WorkCenterId { get; set; }

    /// <summary>The costing period these rates are frozen for.</summary>
    public int CostingPeriodId { get; set; }

    /// <summary>Direct labor rate per labor hour.</summary>
    public decimal LaborRate { get; set; }

    /// <summary>Labor overhead rate per labor hour.</summary>
    public decimal LaborOhRate { get; set; }

    /// <summary>Machine rate per machine hour.</summary>
    public decimal MachineRate { get; set; }

    /// <summary>Variable machine-overhead rate per machine hour.</summary>
    public decimal MachineOhVarRate { get; set; }

    /// <summary>Fixed machine-overhead rate per machine hour.</summary>
    public decimal MachineOhFixedRate { get; set; }

    /// <summary>JSON array of the pool ids that produced these rates, for traceability.</summary>
    public string? SourcePoolIds { get; set; }

    /// <summary>When these rates were frozen.</summary>
    public DateTime? FrozenAt { get; set; }

    /// <summary>Who froze them.</summary>
    [MaxLength(128)]
    public string? FrozenBy { get; set; }

    /// <summary>The work center.</summary>
    [ForeignKey(nameof(WorkCenterId))]
    public WorkCenter? WorkCenter { get; set; }

    /// <summary>The costing period.</summary>
    [ForeignKey(nameof(CostingPeriodId))]
    public CostingPeriod? CostingPeriod { get; set; }
}
