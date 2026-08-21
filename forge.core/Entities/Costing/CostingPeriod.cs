using Forge.Core.Enums;

namespace Forge.Core.Entities.Costing;

/// <summary>A costing period. Standards are frozen for its duration; actuals accumulate against
/// it and variance is the difference. Re-freezing mid-period is an explicit, logged action.</summary>
public class CostingPeriod : BaseAuditableEntity
{
    /// <summary>Inclusive period start.</summary>
    public DateTime StartDate { get; set; }

    /// <summary>Inclusive period end.</summary>
    public DateTime EndDate { get; set; }

    /// <summary>Lifecycle state: Open → Frozen → Closed.</summary>
    public CostingPeriodStatus Status { get; set; } = CostingPeriodStatus.Open;

    /// <summary>When the period's standards were frozen (rates + item costs written).</summary>
    public DateTime? FrozenAt { get; set; }

    /// <summary>When the period was closed (variances posted).</summary>
    public DateTime? ClosedAt { get; set; }
}
