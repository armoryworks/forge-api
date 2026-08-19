using Forge.Core.Enums;

namespace Forge.Core.Entities;

/// <summary>
/// A clock attached to a RESOURCE (lot, permit, sample...) rather than to a step, so it travels with the resource
/// between steps and instances: a lot two days from expiry is two days from expiry wherever it goes.
/// ResourceClock gates read it; the clock job fires it once.
/// </summary>
public class SequenceResourceClock : BaseAuditableEntity
{
    /// <summary>Polymorphic resource (no FK): "Lot", "Permit", ...</summary>
    public string ResourceType { get; set; } = string.Empty;

    public int ResourceId { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public SequenceExpiryAction ExpiryAction { get; set; } = SequenceExpiryAction.Block;

    public string? EscalateRole { get; set; }

    public string? Note { get; set; }

    /// <summary>Set once when the clock job fires the expiry; guards against double escalation.</summary>
    public DateTimeOffset? FiredAt { get; set; }
}
