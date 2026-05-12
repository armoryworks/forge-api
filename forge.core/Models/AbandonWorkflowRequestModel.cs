namespace Forge.Core.Models;

/// <summary>
/// Workflow Pattern Phase 3 — Abandon a workflow run. The server marks the
/// run abandoned and soft-deletes the entity if it is still in
/// <c>status='Draft'</c>.
/// </summary>
public record AbandonWorkflowRequestModel(string? Reason);
