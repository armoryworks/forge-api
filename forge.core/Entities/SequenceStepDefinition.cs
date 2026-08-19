using Forge.Core.Enums;

namespace Forge.Core.Entities;

/// <summary>A step (Petri-net transition) inside a <see cref="SequenceDefinition"/>. Keyed by <see cref="Key"/> within its definition.</summary>
public class SequenceStepDefinition : BaseEntity
{
    public int DefinitionId { get; set; }

    public SequenceDefinition? Definition { get; set; }

    /// <summary>Stable key within the definition, e.g. "cut", "inspect". Edges and gates reference it.</summary>
    public string Key { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Display order only — readiness is decided by edges, never by this number.</summary>
    public int SortOrder { get; set; }

    /// <summary>All predecessors must be complete (default) or any one of them.</summary>
    public SequenceJoinPolicy JoinPolicy { get; set; } = SequenceJoinPolicy.All;

    /// <summary>Optional step clock: maximum minutes a step may sit InProgress before its dwell expires.</summary>
    public int? MaxDwellMinutes { get; set; }

    public SequenceExpiryAction DwellExpiryAction { get; set; } = SequenceExpiryAction.Flag;

    /// <summary>Role notified when the dwell expiry action is Escalate.</summary>
    public string? EscalateRole { get; set; }
}
