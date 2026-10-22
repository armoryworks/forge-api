using System.Text.Json;
using System.Text.Json.Nodes;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Services;
using Forge.Api.Workflows;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Workflows.Runs;

/// <summary>
/// Keep the steps the user skipped on a guided run, so the step rail flags
/// them as not completed after a reload or resume. The ids replace the
/// <see cref="SkippedKey"/> array inside
/// <see cref="Forge.Core.Entities.WorkflowRun.DraftPayload"/>, in definition
/// order; the rest of the payload is left alone. Works before and after the
/// entity exists. Returns 409 once the run is finished or when an id is not a
/// step of the run's definition.
/// </summary>
public record SaveWorkflowSkippedStepsCommand(int RunId, SaveWorkflowSkippedStepsRequestModel Body)
    : IRequest<WorkflowRunResponseModel>
{
    public const string SkippedKey = "skippedStepIds";
}

public class SaveWorkflowSkippedStepsValidator : AbstractValidator<SaveWorkflowSkippedStepsCommand>
{
    public const int MaxSteps = 64;

    public SaveWorkflowSkippedStepsValidator()
    {
        RuleFor(x => x.Body.StepIds).NotNull();
        RuleFor(x => x.Body.StepIds.Count)
            .LessThanOrEqualTo(MaxSteps)
            .When(x => x.Body.StepIds is not null);
        RuleForEach(x => x.Body.StepIds).NotEmpty().MaximumLength(64);
    }
}

public class SaveWorkflowSkippedStepsHandler(
    AppDbContext db,
    ISystemAuditWriter auditWriter,
    IClock clock,
    IHttpContextAccessor httpContextAccessor) : IRequestHandler<SaveWorkflowSkippedStepsCommand, WorkflowRunResponseModel>
{
    public async Task<WorkflowRunResponseModel> Handle(SaveWorkflowSkippedStepsCommand request, CancellationToken ct)
    {
        var run = await db.WorkflowRuns.FirstOrDefaultAsync(r => r.Id == request.RunId, ct)
            ?? throw new KeyNotFoundException($"Workflow run id {request.RunId} not found.");
        WorkflowEntityEditAccess.EnsureCanAccessRun(httpContextAccessor.HttpContext?.User, db.CurrentUserId, run);
        if (run.CompletedAt is not null || run.AbandonedAt is not null)
            throw new InvalidOperationException("Workflow run is no longer active.");

        var def = await db.WorkflowDefinitions.AsNoTracking()
            .FirstOrDefaultAsync(d => d.DefinitionId == run.DefinitionId, ct)
            ?? throw new InvalidOperationException(
                $"Workflow definition '{run.DefinitionId}' missing — run is orphaned.");

        var steps = WorkflowStepHelper.ParseSteps(def.StepsJson);
        var requested = new HashSet<string>(request.Body.StepIds, StringComparer.OrdinalIgnoreCase);
        var unknown = requested.FirstOrDefault(id => WorkflowStepHelper.IndexOf(steps, id) < 0);
        if (unknown is not null)
            throw new InvalidOperationException(
                $"Step '{unknown}' is not part of definition '{def.DefinitionId}'.");
        var skipped = steps.Where(s => requested.Contains(s.Id)).Select(s => s.Id).ToList();

        var payload = ParseObject(run.DraftPayload);
        if (skipped.Count == 0)
            payload.Remove(SaveWorkflowSkippedStepsCommand.SkippedKey);
        else
            payload[SaveWorkflowSkippedStepsCommand.SkippedKey] = new JsonArray(skipped.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray());
        run.DraftPayload = payload.Count == 0 ? null : payload.ToJsonString();
        run.LastActivityAt = clock.UtcNow;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new InvalidOperationException("The run changed while saving; try again.");
        }

        await auditWriter.WriteAsync(
            action: WorkflowAuditEvents.SkippedStepsSaved,
            userId: db.CurrentUserId ?? 0,
            entityType: WorkflowAuditEvents.EntityType,
            entityId: run.Id,
            details: JsonSerializer.Serialize(new { runId = run.Id, skippedStepIds = skipped }),
            ct: ct);

        return run.ToResponse();
    }

    private static JsonObject ParseObject(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return [];
        try
        {
            return JsonNode.Parse(raw) as JsonObject ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
