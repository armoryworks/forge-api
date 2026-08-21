namespace Forge.Core.Enums;

/// <summary>Lifecycle state of a costing period, gating whether rates and rolls may change.</summary>
public enum CostingPeriodStatus
{
    /// <summary>Rates and rolls may still be edited.</summary>
    Open,
    /// <summary>Rates are locked; rolls reference the frozen rates but the period is not yet closed.</summary>
    Frozen,
    /// <summary>Fully closed; no further changes permitted.</summary>
    Closed
}
