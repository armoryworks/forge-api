using Forge.Core.Enums;

namespace Forge.Core.Entities;

/// <summary>Per-run state of one step. One row per step definition, created at instance start.</summary>
public class SequenceStepInstance : BaseEntity
{
    public int InstanceId { get; set; }

    public SequenceInstance? Instance { get; set; }

    public string StepKey { get; set; } = string.Empty;

    public SequenceStepStatus Status { get; set; } = SequenceStepStatus.Pending;

    public DateTimeOffset? ReadyAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public int? StartedByUserId { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public int? CompletedByUserId { get; set; }

    /// <summary>Set for Skipped steps; the reason is mandatory.</summary>
    public string? SkipReason { get; set; }

    /// <summary>StartedAt + MaxDwellMinutes, when the step defines a dwell clock.</summary>
    public DateTimeOffset? DwellExpiresAt { get; set; }

    /// <summary>Set once when the dwell clock fires; the guard against double escalation.</summary>
    public DateTimeOffset? DwellFiredAt { get; set; }
}
