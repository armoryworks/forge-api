namespace Forge.Api.Workflows;

/// <summary>
/// Workflow Pattern Phase 3 — Per-entity-type promotion adapter. After
/// readiness gates pass, the orchestrator delegates to the entity's
/// promoter to flip the status field (e.g. Part.Status: Draft → Active).
/// </summary>
public interface IWorkflowEntityPromoter
{
    string EntityType { get; }

    /// <summary>
    /// Promotes the entity to <paramref name="targetStatus"/>. Caller has
    /// already verified readiness. Returns true on success; false if the
    /// entity is missing or already in the target state.
    /// </summary>
    Task<bool> PromoteAsync(int entityId, string targetStatus, CancellationToken ct);
}
