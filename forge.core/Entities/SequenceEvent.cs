using Forge.Core.Enums;

namespace Forge.Core.Entities;

/// <summary>Append-only history of a run. Never updated or deleted; the audit log and the idempotency record.</summary>
public class SequenceEvent : BaseEntity
{
    public int InstanceId { get; set; }

    public SequenceInstance? Instance { get; set; }

    public SequenceEventType Type { get; set; }

    public string? StepKey { get; set; }

    public string? GateKey { get; set; }

    /// <summary>Free-form JSON detail (verdict, reason, target step, escalate role, ...).</summary>
    public string? PayloadJson { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public int? ActorUserId { get; set; }
}
