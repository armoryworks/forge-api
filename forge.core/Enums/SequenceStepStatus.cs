namespace Forge.Core.Enums;

/// <summary>
/// State of one step inside a running sequence. "Blocked" is deliberately NOT a stored status — it is derived
/// (predecessors satisfied but at least one gate is not Go) so it can never go stale.
/// </summary>
public enum SequenceStepStatus
{
    /// <summary>Waiting on predecessors and/or gates.</summary>
    Pending,
    /// <summary>All predecessors satisfied and every gate reads Go; may be started.</summary>
    Ready,
    InProgress,
    Complete,
    /// <summary>Skipped by an authorised user with a reason; counts as complete for successors.</summary>
    Skipped,
}
