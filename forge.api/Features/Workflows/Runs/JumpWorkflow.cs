using System.Text.Json;

using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Services;
using Forge.Api.Workflows;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Workflows.Runs;

/// <summary>
/// Workflow Pattern Phase 3 / D2 — Jump to a specific step. Allowed when:
///   • target is the current step (no-op);
///   • target is to the LEFT of current (always allowed — earlier completed step);
///   • target is to the RIGHT of current AND every step between current and
///     target has its completion gates satisfied (forward jump = same as
///     advancing through them in sequence).
/// </summary>
public record JumpWorkflowCommand(int RunId, JumpWorkflowRequestModel Body)
    : IRequest<WorkflowRunResponseModel>;

public class JumpWorkflowValidator : AbstractValidator<JumpWorkflowCommand>
{
    public JumpWorkflowValidator()
    {
        RuleFor(x => x.Body.TargetStepId).NotEmpty().MaximumLength(64);
    }
}

public class JumpWorkflowHandler(
    AppDbContext db,
    IEntityReadinessService readiness,
    ISystemAuditWriter auditWriter,
    IClock clock) : IRequestHandler<JumpWorkflowCommand, WorkflowRunResponseModel>
{
    public async Task<WorkflowRunResponseModel> Handle(JumpWorkflowCommand request, CancellationToken ct)
    {
        var run = await db.WorkflowRuns.FirstOrDefaultAsync(r => r.Id == request.RunId, ct)
            ?? throw new KeyNotFoundException($"Workflow run id {request.RunId} not found.");
        if (run.CompletedAt is not null || run.AbandonedAt is not null)
            throw new InvalidOperationException("Workflow run is no longer active.");

        var def = await db.WorkflowDefinitions.AsNoTracking()
            .FirstOrDefaultAsync(d => d.DefinitionId == run.DefinitionId, ct)
            ?? throw new InvalidOperationException(
                $"Workflow definition '{run.DefinitionId}' missing — run is orphaned.");

        var steps = WorkflowStepHelper.ParseSteps(def.StepsJson);
        var targetIdx = WorkflowStepHelper.IndexOf(steps, request.Body.TargetStepId);
        if (targetIdx < 0)
            throw new InvalidOperationException(
                $"Step '{request.Body.TargetStepId}' is not part of definition '{def.DefinitionId}'.");

        var currentIdx = WorkflowStepHelper.IndexOf(steps, run.CurrentStepId);
        if (currentIdx < 0) currentIdx = 0; // fall back to first step on orphaned current

        // Forward jump: every step between current and target (exclusive of
        // target itself, inclusive of current) must satisfy its gates.
        if (targetIdx > currentIdx)
        {
            // Deferred materialization: no entity yet means no readiness can
            // pass. Surface a clean 409 listing only the gates of the steps
            // we'd be jumping over — irrelevant validators (e.g. hasBom on a
            // raw-material workflow that doesn't gate on BOM) stay hidden.
            //
            // Each row carries BlockingStepId/LabelKey so the UI can render
            // "Finish 'Basics' first" instead of a generic "an earlier step
            // is incomplete" — internal step IDs ('sourcing' etc.) stay
            // hidden behind the translated LabelKey.
            if (run.EntityId is null)
            {
                // Map every blocked validator id back to the FIRST step it
                // gates within the jump range. That step is the one the user
                // has to finish first; later steps may also reference the
                // same validator but the first is the actionable one.
                var firstBlockingStep = new Dictionary<string, WorkflowStepDefinition>(StringComparer.OrdinalIgnoreCase);
                for (var i = currentIdx; i < targetIdx; i++)
                    foreach (var gate in steps[i].CompletionGates)
                        firstBlockingStep.TryAdd(gate, steps[i]);

                var blockingGateIds = firstBlockingStep.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
                var blockingValidators = await db.EntityReadinessValidators
                    .AsNoTracking()
                    .Where(v => v.EntityType == run.EntityType && blockingGateIds.Contains(v.ValidatorId))
                    .ToListAsync(ct);
                var payloadAll = blockingValidators.Select(v =>
                {
                    var step = firstBlockingStep[v.ValidatorId];
                    return new MissingValidatorResponseModel(
                        v.ValidatorId, v.DisplayNameKey, v.MissingMessageKey,
                        BlockingStepId: step.Id,
                        BlockingStepLabelKey: step.LabelKey);
                }).ToList();
                var firstStepName = steps[currentIdx];
                throw new WorkflowMissingValidatorsException(
                    payloadAll,
                    $"Finish '{firstStepName.LabelKey}' before moving on.");
            }

            var missing = await readiness.GetMissingValidatorsAsync(run.EntityType, run.EntityId.Value, ct);
            var failing = missing.Select(m => m.ValidatorId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            for (var i = currentIdx; i < targetIdx; i++)
            {
                foreach (var gate in steps[i].CompletionGates)
                {
                    if (!failing.Contains(gate)) continue;
                    var blockingStep = steps[i];
                    throw new WorkflowMissingValidatorsException(
                        missing.Where(m => blockingStep.CompletionGates.Contains(m.ValidatorId, StringComparer.OrdinalIgnoreCase))
                            .Select(m => new MissingValidatorResponseModel(
                                m.ValidatorId, m.DisplayNameKey, m.MissingMessageKey,
                                BlockingStepId: blockingStep.Id,
                                BlockingStepLabelKey: blockingStep.LabelKey))
                            .ToList(),
                        $"Finish '{blockingStep.LabelKey}' before moving on.");
                }
            }
        }

        run.CurrentStepId = steps[targetIdx].Id;
        run.LastActivityAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(
            action: WorkflowAuditEvents.JumpedTo,
            userId: db.CurrentUserId ?? 0,
            entityType: WorkflowAuditEvents.EntityType,
            entityId: run.Id,
            details: JsonSerializer.Serialize(new
            {
                runId = run.Id,
                from = currentIdx >= 0 && currentIdx < steps.Count ? steps[currentIdx].Id : null,
                to = run.CurrentStepId,
            }),
            ct: ct);

        return run.ToResponse();
    }
}
