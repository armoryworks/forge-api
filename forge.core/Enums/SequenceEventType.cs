namespace Forge.Core.Enums;

/// <summary>Append-only event kinds recorded on <see cref="Entities.SequenceEvent"/>. This IS the audit trail.</summary>
public enum SequenceEventType
{
    InstanceStarted,
    StepReady,
    StepBlocked,
    StepStarted,
    StepCompleted,
    StepSkipped,
    StepReset,
    GateEvaluated,
    GateCleared,
    GateOverridden,
    ClockExpired,
    Escalated,
    Reworked,
    InstanceCompleted,
    InstanceCancelled,
}
