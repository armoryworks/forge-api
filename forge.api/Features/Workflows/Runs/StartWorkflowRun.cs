using System.Text.Json;

using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Services;
using Forge.Api.Workflows;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Workflows.Runs;

/// <summary>
/// Workflow Pattern Phase 3 — Start a new workflow run. The entity row is
/// NOT created here; it materializes when the first step (the materialization
/// step) submits valid data via <see cref="PatchWorkflowStepHandler"/>. Until
/// then, <see cref="WorkflowRun.EntityId"/> is null and any initial payload
/// supplied by the client is held in <see cref="WorkflowRun.DraftPayload"/>.
/// This avoids creating placeholder ("(Draft)") rows for workflows that the
/// user might abandon before filling in the basics.
/// </summary>
public record StartWorkflowRunCommand(StartWorkflowRunRequestModel Body)
    : IRequest<WorkflowRunResponseModel>;

public class StartWorkflowRunValidator : AbstractValidator<StartWorkflowRunCommand>
{
    public StartWorkflowRunValidator()
    {
        RuleFor(x => x.Body.EntityType).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Body.DefinitionId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Body.Mode)
            .Must(m => m is null || m == "express" || m == "guided")
            .WithMessage("mode must be 'express' or 'guided' when supplied.");
    }
}

public class StartWorkflowRunHandler(
    AppDbContext db,
    ISystemAuditWriter auditWriter,
    IClock clock) : IRequestHandler<StartWorkflowRunCommand, WorkflowRunResponseModel>
{
    public async Task<WorkflowRunResponseModel> Handle(StartWorkflowRunCommand request, CancellationToken ct)
    {
        var body = request.Body;

        // Resolve definition (must exist and match entity type).
        var def = await db.WorkflowDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.DefinitionId == body.DefinitionId, ct)
            ?? throw new KeyNotFoundException($"Workflow definition '{body.DefinitionId}' not found.");
        if (!string.Equals(def.EntityType, body.EntityType, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Definition '{def.DefinitionId}' targets entity type '{def.EntityType}', not '{body.EntityType}'.");

        // Compute first step from the definition's StepsJson.
        var firstStepId = ReadFirstStepId(def.StepsJson);
        var mode = body.Mode ?? def.DefaultMode;
        var now = clock.UtcNow;

        // Stash the initial payload (if any) for the first step's materialize
        // branch to merge with its field patch. We hold the raw JSON text so
        // the column is jsonb-shaped and the patch handler can re-parse it.
        var draftPayload = body.InitialEntityData?.ValueKind == JsonValueKind.Object
            ? body.InitialEntityData.Value.GetRawText()
            : null;

        var run = new WorkflowRun
        {
            EntityType = body.EntityType,
            EntityId = null,
            DraftPayload = draftPayload,
            DefinitionId = body.DefinitionId,
            CurrentStepId = firstStepId,
            Mode = mode,
            StartedAt = now,
            StartedByUserId = db.CurrentUserId ?? 0,
            LastActivityAt = now,
        };
        db.WorkflowRuns.Add(run);
        // No junction row inserted yet — WorkflowRunEntity carries a non-null
        // EntityId in its composite PK, so we wait until materialization.

        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(
            action: WorkflowAuditEvents.Started,
            userId: db.CurrentUserId ?? 0,
            entityType: WorkflowAuditEvents.EntityType,
            entityId: run.Id,
            details: JsonSerializer.Serialize(new
            {
                runId = run.Id,
                entityType = run.EntityType,
                entityId = run.EntityId,
                definitionId = run.DefinitionId,
                mode = run.Mode,
                currentStepId = run.CurrentStepId,
            }),
            ct: ct);

        return run.ToResponse();
    }

    private static string? ReadFirstStepId(string stepsJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(stepsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            foreach (var step in doc.RootElement.EnumerateArray())
            {
                if (step.ValueKind == JsonValueKind.Object &&
                    step.TryGetProperty("id", out var id) &&
                    id.ValueKind == JsonValueKind.String)
                {
                    return id.GetString();
                }
            }
        }
        catch (JsonException) { }
        return null;
    }
}
