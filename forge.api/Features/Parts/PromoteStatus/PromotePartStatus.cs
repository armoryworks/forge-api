using System.Text.Json;

using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Services;
using Forge.Api.Workflows;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Parts.PromoteStatus;

/// <summary>
/// Workflow Pattern Phase 3 — Authoritative Part status promotion gate
/// (Draft → Active in v1; future transitions plug into the same handler).
/// Runs the entity readiness validators server-side; failures return 409
/// with the missing-validators envelope so callers can render
/// "Missing: BOM, Routing" with jump-to links.
///
/// The workflow Mark-Complete button delegates to this same handler — no
/// duplicate completion logic.
/// </summary>
public record PromotePartStatusCommand(int PartId, PromoteEntityStatusRequestModel Body)
    : IRequest<PartDetailResponseModel>;

public class PromotePartStatusValidator : AbstractValidator<PromotePartStatusCommand>
{
    public PromotePartStatusValidator()
    {
        RuleFor(x => x.Body.TargetStatus)
            .NotEmpty()
            .Must(s => s is "Active" or "Obsolete" or "Prototype")
            .WithMessage("targetStatus must be Active, Obsolete, or Prototype.");
    }
}

public class PromotePartStatusHandler(
    AppDbContext db,
    IPartRepository repo,
    IEntityReadinessService readiness,
    IEnumerable<IWorkflowEntityPromoter> promoters,
    ISystemAuditWriter auditWriter,
    IClock clock) : IRequestHandler<PromotePartStatusCommand, PartDetailResponseModel>
{
    private readonly Dictionary<string, IWorkflowEntityPromoter> _promoters =
        promoters.ToDictionary(p => p.EntityType, StringComparer.OrdinalIgnoreCase);

    public async Task<PartDetailResponseModel> Handle(PromotePartStatusCommand request, CancellationToken ct)
    {
        const string EntityType = "Part";

        // If a workflow run is in flight against this part, scope the readiness
        // check to that run's required gates — same shape CompleteWorkflowRun
        // uses. This keeps the two completion paths (Mark Complete in the
        // workflow vs. Promote on the entity page) in lockstep, and prevents
        // global validators outside the run's gates (e.g. hasSourcing on a
        // Make+Subassembly part) from blocking promotion.
        var run = await db.WorkflowRuns
            .FirstOrDefaultAsync(r => r.EntityType == EntityType
                                      && r.EntityId == request.PartId
                                      && r.CompletedAt == null
                                      && r.AbandonedAt == null, ct);

        var missing = await readiness.GetMissingValidatorsAsync(EntityType, request.PartId, ct);

        // When a run is in flight, scope readiness to the run's required-step
        // gates AND build a validator-id → first-required-step map so the 409
        // envelope can name the step the user still needs to finish.
        var firstBlockingStep = new Dictionary<string, WorkflowStepDefinition>(StringComparer.OrdinalIgnoreCase);
        if (run is not null)
        {
            var def = await db.WorkflowDefinitions.AsNoTracking()
                .FirstOrDefaultAsync(d => d.DefinitionId == run.DefinitionId, ct);
            if (def is not null)
            {
                var requiredSteps = WorkflowStepHelper.ParseSteps(def.StepsJson)
                    .Where(s => s.Required)
                    .ToList();
                var requiredGateIds = requiredSteps
                    .SelectMany(s => s.CompletionGates)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                missing = [.. missing.Where(m => requiredGateIds.Contains(m.ValidatorId))];
                foreach (var step in requiredSteps)
                    foreach (var gate in step.CompletionGates)
                        firstBlockingStep.TryAdd(gate, step);
            }
        }

        if (missing.Count > 0)
        {
            var payload = missing.Select(m =>
            {
                firstBlockingStep.TryGetValue(m.ValidatorId, out var step);
                return new MissingValidatorResponseModel(
                    m.ValidatorId, m.DisplayNameKey, m.MissingMessageKey,
                    BlockingStepId: step?.Id,
                    BlockingStepLabelKey: step?.LabelKey);
            }).ToList();
            throw new WorkflowMissingValidatorsException(
                payload,
                $"Cannot promote Part {request.PartId} to {request.Body.TargetStatus} — readiness validators not satisfied.");
        }

        if (!_promoters.TryGetValue(EntityType, out var promoter))
            throw new InvalidOperationException(
                $"No workflow entity promoter registered for entity type '{EntityType}'.");

        await promoter.PromoteAsync(request.PartId, request.Body.TargetStatus, ct);

        // Best-effort: if the run is in flight and we landed on Active, mark
        // the run complete in the same operation so callers see one fewer race.
        if (run is not null)
        {
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
                    targetStatus = request.Body.TargetStatus,
                }),
                ct: ct);
        }

        await auditWriter.WriteAsync(
            action: WorkflowAuditEvents.EntityStatusPromoted,
            userId: db.CurrentUserId ?? 0,
            entityType: EntityType,
            entityId: request.PartId,
            details: JsonSerializer.Serialize(new
            {
                entityType = EntityType,
                entityId = request.PartId,
                targetStatus = request.Body.TargetStatus,
                workflowRunId = run?.Id,
            }),
            ct: ct);

        return (await repo.GetDetailAsync(request.PartId, ct))
            ?? throw new KeyNotFoundException($"Part id {request.PartId} not found after promotion.");
    }
}
