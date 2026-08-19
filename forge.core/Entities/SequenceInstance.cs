using Forge.Core.Enums;

namespace Forge.Core.Entities;

/// <summary>
/// A run of one published <see cref="SequenceDefinition"/> version against an optional polymorphic subject
/// (e.g. Job/123). Its marking lives in <see cref="Steps"/>/<see cref="Gates"/>; its history in <see cref="Events"/>.
/// </summary>
public class SequenceInstance : BaseAuditableEntity, IConcurrencyVersioned
{
    public int DefinitionId { get; set; }

    public SequenceDefinition? Definition { get; set; }

    /// <summary>Polymorphic subject (no FK): "Job", "Lot", "Permit", ... Null for a free-standing run.</summary>
    public string? SubjectEntityType { get; set; }

    public int? SubjectEntityId { get; set; }

    public SequenceInstanceStatus Status { get; set; } = SequenceInstanceStatus.Running;

    public DateTimeOffset StartedAt { get; set; }

    public int? StartedByUserId { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public DateTimeOffset? CancelledAt { get; set; }

    public int? CancelledByUserId { get; set; }

    public string? CancelReason { get; set; }

    /// <summary>Optimistic concurrency token (uint, bumped by AppDbContext per IConcurrencyVersioned).</summary>
    public uint Version { get; set; } = 1;

    public ICollection<SequenceStepInstance> Steps { get; set; } = [];

    public ICollection<SequenceGateInstance> Gates { get; set; } = [];

    public ICollection<SequenceEvent> Events { get; set; } = [];
}
