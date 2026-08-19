using Forge.Core.Enums;

namespace Forge.Core.Entities;

/// <summary>Per-run state of one gate. One row per gate definition, created at instance start.</summary>
public class SequenceGateInstance : BaseEntity
{
    public int InstanceId { get; set; }

    public SequenceInstance? Instance { get; set; }

    public string StepKey { get; set; } = string.Empty;

    public string GateKey { get; set; } = string.Empty;

    public SequenceGateVerdict Verdict { get; set; } = SequenceGateVerdict.Unknown;

    public DateTimeOffset? LastEvaluatedAt { get; set; }

    /// <summary>Why the source answered as it did (shown as the "blocked because" text).</summary>
    public string? Reason { get; set; }

    /// <summary>ManualClearance: the recorded clearance. The record IS the sign-off.</summary>
    public DateTimeOffset? ClearedAt { get; set; }

    public int? ClearedByUserId { get; set; }

    /// <summary>A forced Go. Sticky until the step is reset by rework. Reason is mandatory.</summary>
    public DateTimeOffset? OverriddenAt { get; set; }

    public int? OverriddenByUserId { get; set; }

    public string? OverrideReason { get; set; }
}
