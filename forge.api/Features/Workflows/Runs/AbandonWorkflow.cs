using System.Text.Json;

using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Services;
using Forge.Api.Workflows;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Workflows.Runs;

/// <summary>
/// Workflow Pattern Phase 3 — Abandon the run. Marks the run abandoned and
/// soft-deletes the entity if it is still in <c>status='Draft'</c>. If the
/// entity has already been promoted (or the user opened a workflow against
/// an existing entity), the run is abandoned but the entity stays.
///
/// When the run was abandoned before its first step ran, the entity row was
/// never materialized (<see cref="WorkflowRun.EntityId"/> is null) — there
/// is nothing to soft-delete and no orphan row left behind.
/// </summary>
public record AbandonWorkflowCommand(int RunId, AbandonWorkflowRequestModel Body)
    : IRequest<WorkflowRunResponseModel>;

public class AbandonWorkflowHandler(
    AppDbContext db,
    IEnumerable<IWorkflowFieldApplier> appliers,
    ISystemAuditWriter auditWriter,
    IClock clock) : IRequestHandler<AbandonWorkflowCommand, WorkflowRunResponseModel>
{
    private readonly Dictionary<string, IWorkflowFieldApplier> _appliers =
        appliers.ToDictionary(a => a.EntityType, StringComparer.OrdinalIgnoreCase);

    public async Task<WorkflowRunResponseModel> Handle(AbandonWorkflowCommand request, CancellationToken ct)
    {
        var run = await db.WorkflowRuns.FirstOrDefaultAsync(r => r.Id == request.RunId, ct)
            ?? throw new KeyNotFoundException($"Workflow run id {request.RunId} not found.");
        if (run.AbandonedAt is not null) return run.ToResponse(); // idempotent
        if (run.CompletedAt is not null)
            throw new InvalidOperationException("Cannot abandon a completed run.");

        // Soft-delete the entity if still Draft. If the entity wasn't created
        // (deferred materialization, abandoned before first step), nothing to
        // delete — the run row alone records the abandonment.
        if (run.EntityId is int entityId && _appliers.TryGetValue(run.EntityType, out var applier))
        {
            await applier.SoftDeleteIfDraftAsync(entityId, ct);
        }

        run.AbandonedAt = clock.UtcNow;
        run.AbandonedReason = string.IsNullOrWhiteSpace(request.Body.Reason) ? "user" : request.Body.Reason;
        if (run.AbandonedReason!.Length > 64) run.AbandonedReason = run.AbandonedReason[..64];
        run.LastActivityAt = clock.UtcNow;

        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(
            action: WorkflowAuditEvents.Abandoned,
            userId: db.CurrentUserId ?? 0,
            entityType: WorkflowAuditEvents.EntityType,
            entityId: run.Id,
            details: JsonSerializer.Serialize(new
            {
                runId = run.Id,
                reason = run.AbandonedReason,
                entityType = run.EntityType,
                entityId = run.EntityId,
            }),
            ct: ct);

        return run.ToResponse();
    }
}
