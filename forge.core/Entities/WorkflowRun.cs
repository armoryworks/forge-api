namespace Forge.Core.Entities;

/// <summary>
/// Workflow Pattern Phase 2 — UX metadata for an in-flight or completed
/// workflow run against a single primary entity. Per D6, step completion is
/// derived from the entity's data state (entity readiness validators), not
/// stored on this row. WorkflowRun only tracks user-experience metadata so
/// the user can resume / switch mode / abandon.
///
/// Polymorphic by (EntityType, EntityId). UNIQUE on those two columns —
/// at most one in-flight run per entity (Q1 — one run per entity).
///
/// IConcurrencyVersioned: parallels Phase 4's CapabilitySnapshot decision —
/// uint Version for InMemory test compatibility instead of raw xmin.
/// </summary>
public class WorkflowRun : BaseAuditableEntity, IConcurrencyVersioned
{
    /// <summary>Primary entity type, e.g. "Part", "Customer", "Quote".</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>
    /// Primary entity id (no FK; polymorphic). Nullable: the entity row is
    /// not materialized until the workflow's first step (the materialization
    /// step) submits valid data. While null, the in-flight initial payload
    /// lives in <see cref="DraftPayload"/>.
    /// </summary>
    public int? EntityId { get; set; }

    /// <summary>
    /// In-flight initial payload (raw JSON) held until the first step
    /// materializes the entity. Once <see cref="EntityId"/> is set, this
    /// column is cleared.
    /// </summary>
    public string? DraftPayload { get; set; }

    /// <summary>The WorkflowDefinition.id pinned at run start (Q2).</summary>
    public string DefinitionId { get; set; } = string.Empty;

    /// <summary>Resume target: last step the user was on. Null while pending advance.</summary>
    public string? CurrentStepId { get; set; }

    /// <summary>'express' | 'guided' (D4 — switchable at any time).</summary>
    public string Mode { get; set; } = "guided";

    public DateTimeOffset StartedAt { get; set; }
    public int StartedByUserId { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? AbandonedAt { get; set; }

    /// <summary>'expired' | 'user' | 'definition-deprecated' etc.</summary>
    public string? AbandonedReason { get; set; }

    public DateTimeOffset LastActivityAt { get; set; }

    /// <summary>Optimistic concurrency token (uint, monotonically incrementing).</summary>
    public uint Version { get; set; } = 1;

    /// <summary>Junction rows for cross-entity workflows (Q3).</summary>
    public ICollection<WorkflowRunEntity> RunEntities { get; set; } = [];
}
