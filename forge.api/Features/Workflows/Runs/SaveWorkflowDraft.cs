using System.Text.Json;
using System.Text.Json.Nodes;

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
/// Keep what the user has typed on a run whose entity has not materialized
/// yet, so the draft can be listed under that text and the first step can be
/// refilled on resume. The fields replace the <see cref="TypedKey"/> object
/// inside <see cref="Forge.Core.Entities.WorkflowRun.DraftPayload"/>; the
/// rest of the payload (the fork-dialog picks) is left alone. Entity creators
/// read only top-level keys they know, so the typed text never feeds
/// materialization — the first step patch still carries the real values.
/// Returns 409 once the run is finished or its entity exists.
/// </summary>
public record SaveWorkflowDraftCommand(int RunId, SaveWorkflowDraftRequestModel Body)
    : IRequest<WorkflowRunResponseModel>
{
    public const string TypedKey = "typed";
}

public class SaveWorkflowDraftValidator : AbstractValidator<SaveWorkflowDraftCommand>
{
    public const int MaxFieldsLength = 4000;

    public SaveWorkflowDraftValidator()
    {
        RuleFor(x => x.Body.Fields.ValueKind)
            .Equal(JsonValueKind.Object)
            .WithMessage("fields must be an object.");
        RuleFor(x => x.Body.Fields)
            .Must(fields => fields.ValueKind != JsonValueKind.Object || fields.GetRawText().Length <= MaxFieldsLength)
            .WithMessage($"fields must be at most {MaxFieldsLength} characters of JSON.");
    }
}

public class SaveWorkflowDraftHandler(
    AppDbContext db,
    ISystemAuditWriter auditWriter,
    IClock clock) : IRequestHandler<SaveWorkflowDraftCommand, WorkflowRunResponseModel>
{
    public async Task<WorkflowRunResponseModel> Handle(SaveWorkflowDraftCommand request, CancellationToken ct)
    {
        var run = await db.WorkflowRuns.FirstOrDefaultAsync(r => r.Id == request.RunId, ct)
            ?? throw new KeyNotFoundException($"Workflow run id {request.RunId} not found.");
        if (run.CompletedAt is not null || run.AbandonedAt is not null)
            throw new InvalidOperationException("Workflow run is no longer active.");
        if (run.EntityId is not null)
            throw new InvalidOperationException("The draft has already been saved; edit the record instead.");

        var payload = ParseObject(run.DraftPayload);
        payload[SaveWorkflowDraftCommand.TypedKey] = JsonNode.Parse(request.Body.Fields.GetRawText());
        run.DraftPayload = payload.ToJsonString();
        run.LastActivityAt = clock.UtcNow;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new InvalidOperationException("The draft changed while saving; try again.");
        }

        await auditWriter.WriteAsync(
            action: WorkflowAuditEvents.DraftSaved,
            userId: db.CurrentUserId ?? 0,
            entityType: WorkflowAuditEvents.EntityType,
            entityId: run.Id,
            details: JsonSerializer.Serialize(new { runId = run.Id }),
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
