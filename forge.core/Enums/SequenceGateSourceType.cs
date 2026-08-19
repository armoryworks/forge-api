namespace Forge.Core.Enums;

/// <summary>
/// What a gate checks against. Built-in sources ship with the engine; <see cref="Custom"/> is resolved by
/// <c>config.key</c> against a registered <see cref="Sequences.IGateSource"/> so modules can add their own
/// (materials readiness, permit validity, ...) without touching the engine.
/// </summary>
public enum SequenceGateSourceType
{
    /// <summary>Go once an authorised person records a clearance on the gate instance.</summary>
    ManualClearance,
    /// <summary>Go while "now" is inside the configured [notBefore, notAfter] window.</summary>
    TimeWindow,
    /// <summary>Go while the referenced resource's <see cref="Entities.SequenceResourceClock"/> is unexpired.</summary>
    ResourceClock,
    /// <summary>Go when a terminal, approved ApprovalRequest exists for the referenced entity.</summary>
    Approval,
    /// <summary>Resolved by a module-registered gate source; an unknown key fails closed (NoGo).</summary>
    Custom,
}
