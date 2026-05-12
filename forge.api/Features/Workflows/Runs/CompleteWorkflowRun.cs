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
/// Workflow Pattern Phase 3 — Mark Complete. This is sugar for "promote
/// entity status" per the design doc — the same readiness gate runs here as
/// when the user clicks Promote on the entity detail page directly.
///
/// Phase 3 wires Part → Active. Other entity types follow the same shape
/// once their promoters are registered.
/// </summary>
public record CompleteWorkflowRunCommand(int RunId) : IRequest<WorkflowRunResponseModel>;

public class CompleteWorkflowRunHandler(
    AppDbContext db,
    IEntityReadinessService readiness,
    IEnumerable<IWorkflowEntityPromoter> promoters,
    ISystemAuditWriter auditWriter,
    IClock clock) : IRequestHandler<CompleteWorkflowRunCommand, WorkflowRunResponseModel>
{
    private readonly Dictionary<string, IWorkflowEntityPromoter> _promoters =
        promoters.ToDictionary(p => p.EntityType, StringComparer.OrdinalIgnoreCase);

    public async Task<WorkflowRunResponseModel> Handle(CompleteWorkflowRunCommand request, CancellationToken ct)
    {
        var run = await db.WorkflowRuns.FirstOrDefaultAsync(r => r.Id == request.RunId, ct)
            ?? throw new KeyNotFoundException($"Workflow run id {request.RunId} not found.");
        if (run.CompletedAt is not null) return run.ToResponse(); // idempotent
        if (run.AbandonedAt is not null)
            throw new InvalidOperationException("Cannot complete an abandoned run.");

        // Resolve the workflow definition so we can scope the readiness check
        // to the gates this particular run cares about. Different definitions
        // gate on different validator subsets — e.g. raw-material express only
        // needs hasBasics + hasCost; assembly guided needs all four. We never
        // want to surface validators outside the run's declared gates because
        // those parts of the entity model don't apply to this workflow.
        var def = await db.WorkflowDefinitions.AsNoTracking()
            .FirstOrDefaultAsync(d => d.DefinitionId == run.DefinitionId, ct)
            ?? throw new InvalidOperationException(
                $"Workflow definition '{run.DefinitionId}' missing — run is orphaned.");
        var requiredGateIds = WorkflowStepHelper.ParseSteps(def.StepsJson)
            .Where(s => s.Required)
            .SelectMany(s => s.CompletionGates)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Deferred materialization: the entity hasn't been created yet, which
        // means the user hasn't completed the first step. Surface this as a
        // readiness failure scoped to the run's required gates.
        if (run.EntityId is null)
        {
            var defValidators = await db.EntityReadinessValidators
                .AsNoTracking()
                .Where(v => v.EntityType == run.EntityType && requiredGateIds.Contains(v.ValidatorId))
                .ToListAsync(ct);
            var payloadAll = defValidators.Select(v => new MissingValidatorResponseModel(
                v.ValidatorId, v.DisplayNameKey, v.MissingMessageKey)).ToList();
            throw new WorkflowMissingValidatorsException(
                payloadAll,
                $"Cannot complete workflow {run.Id} — entity has not been created yet.");
        }

        var missing = await readiness.GetMissingValidatorsAsync(run.EntityType, run.EntityId.Value, ct);
        var requiredMissing = missing
            .Where(m => requiredGateIds.Contains(m.ValidatorId))
            .ToList();
        if (requiredMissing.Count > 0)
        {
            var payload = requiredMissing.Select(m => new MissingValidatorResponseModel(
                m.ValidatorId, m.DisplayNameKey, m.MissingMessageKey)).ToList();
            throw new WorkflowMissingValidatorsException(
                payload,
                $"Cannot complete workflow {run.Id} — readiness validators not satisfied.");
        }

        if (!_promoters.TryGetValue(run.EntityType, out var promoter))
            throw new InvalidOperationException(
                $"No workflow entity promoter registered for entity type '{run.EntityType}'.");

        // v1: promote to "Active" (the only canonical target). Future variants
        // can carry a target_status column on workflow_runs if needed.
        await promoter.PromoteAsync(run.EntityId.Value, "Active", ct);

        run.CompletedAt = clock.UtcNow;
        run.LastActivityAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(
            action: WorkflowAuditEvents.Completed,
            userId: db.CurrentUserId ?? 0,
            entityType: WorkflowAuditEvents.EntityType,
            entityId: run.Id,
            details: JsonSerializer.Serialize(new
            {
                runId = run.Id,
                entityType = run.EntityType,
                entityId = run.EntityId,
                targetStatus = "Active",
            }),
            ct: ct);

        return run.ToResponse();
    }
}
