namespace Forge.Core.Models;

/// <summary>
/// Workflow Pattern Phase 3 — Workflow run response model. Per D6, step
/// completion is derived from the entity's current state — not stored on
/// the run row — so this model carries only UX metadata. The client (or
/// admin UI) cross-references entity validators to compute step state.
/// </summary>
public record WorkflowRunResponseModel(
    int Id,
    string EntityType,
    int? EntityId,
    string DefinitionId,
    string? CurrentStepId,
    string Mode,
    DateTimeOffset StartedAt,
    int StartedByUserId,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? AbandonedAt,
    string? AbandonedReason,
    DateTimeOffset LastActivityAt,
    uint Version);
