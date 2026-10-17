using System.Text.Json;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Middleware;
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
/// <para>
/// When the body carries an <c>EntityId</c> the run is bound to that existing
/// entity up front (EntityId + primary junction row), so the steps read and
/// write the saved record and readiness gates evaluate against it. Starting a
/// bound run is idempotent: an active run for the same entity + definition is
/// returned instead of opening a second one, and a completed or abandoned run
/// for the entity is reopened at the first step, since an entity holds a
/// single run row. Binding requires the roles of the
/// entity's own update endpoint, and an unbound run the roles of its create
/// endpoint (<see cref="WorkflowEntityEditAccess"/>).
/// </para>
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
        RuleFor(x => x.Body.EntityId)
            .GreaterThan(0)
            .When(x => x.Body.EntityId.HasValue);
    }
}

public class StartWorkflowRunHandler(
    AppDbContext db,
    ISystemAuditWriter auditWriter,
    IClock clock,
    IEnumerable<IEntityReadinessLoader> loaders,
    IHttpContextAccessor httpContextAccessor) : IRequestHandler<StartWorkflowRunCommand, WorkflowRunResponseModel>
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

        if (body.EntityId is int boundId)
        {
            if (!WorkflowEntityEditAccess.CanEdit(httpContextAccessor.HttpContext?.User, def.EntityType))
                throw new ForbiddenException(
                    $"Your role cannot edit an existing {def.EntityType}, so it cannot walk a guided workflow over one.");
            var loader = loaders.FirstOrDefault(l =>
                string.Equals(l.EntityType, def.EntityType, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException(
                    $"Entity type '{def.EntityType}' cannot be bound to a workflow run.");
            _ = await loader.LoadAsync(boundId, ct)
                ?? throw new KeyNotFoundException($"{def.EntityType} id {boundId} not found.");

            var existing = await db.WorkflowRuns
                .FirstOrDefaultAsync(r => r.EntityType == body.EntityType && r.EntityId == boundId, ct);
            if (existing is not null)
                return await ReuseBoundRunAsync(existing, def, body.Mode, ct);
        }
        else if (!WorkflowEntityEditAccess.CanCreate(httpContextAccessor.HttpContext?.User, def.EntityType))
        {
            throw new ForbiddenException(
                $"Your role cannot create a {def.EntityType}, so it cannot start a guided workflow for a new one.");
        }

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
            EntityId = body.EntityId,
            DraftPayload = body.EntityId is null ? draftPayload : null,
            DefinitionId = body.DefinitionId,
            CurrentStepId = firstStepId,
            Mode = mode,
            StartedAt = now,
            StartedByUserId = db.CurrentUserId ?? 0,
            LastActivityAt = now,
        };
        db.WorkflowRuns.Add(run);
        if (body.EntityId is int entityId)
        {
            db.WorkflowRunEntities.Add(new WorkflowRunEntity
            {
                Run = run,
                EntityType = body.EntityType,
                EntityId = entityId,
                Role = "primary",
            });
        }

        await db.SaveChangesAsync(ct);
        await WriteStartedAuditAsync(run, ct);

        return run.ToResponse();
    }

    private async Task<WorkflowRunResponseModel> ReuseBoundRunAsync(
        WorkflowRun run, WorkflowDefinition def, string? requestedMode, CancellationToken ct)
    {
        var active = run.CompletedAt == null && run.AbandonedAt == null;
        if (active)
        {
            if (run.DefinitionId == def.DefinitionId)
                return run.ToResponse();
            throw new InvalidOperationException(
                $"{run.EntityType} id {run.EntityId} already has an open '{run.DefinitionId}' workflow. Finish or abandon it first.");
        }

        var now = clock.UtcNow;
        run.DefinitionId = def.DefinitionId;
        run.CurrentStepId = ReadFirstStepId(def.StepsJson);
        run.Mode = requestedMode ?? def.DefaultMode;
        run.DraftPayload = null;
        run.CompletedAt = null;
        run.AbandonedAt = null;
        run.AbandonedReason = null;
        run.StartedAt = now;
        run.StartedByUserId = db.CurrentUserId ?? 0;
        run.LastActivityAt = now;

        var hasPrimary = await db.WorkflowRunEntities.AnyAsync(e => e.RunId == run.Id
            && e.EntityType == run.EntityType && e.EntityId == run.EntityId, ct);
        if (!hasPrimary)
        {
            db.WorkflowRunEntities.Add(new WorkflowRunEntity
            {
                RunId = run.Id,
                EntityType = run.EntityType,
                EntityId = run.EntityId!.Value,
                Role = "primary",
            });
        }

        await db.SaveChangesAsync(ct);
        await WriteStartedAuditAsync(run, ct);
        return run.ToResponse();
    }

    private Task WriteStartedAuditAsync(WorkflowRun run, CancellationToken ct) =>
        auditWriter.WriteAsync(
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
