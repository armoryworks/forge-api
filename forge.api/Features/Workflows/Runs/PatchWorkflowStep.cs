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
/// Workflow Pattern Phase 3 — Save the current step's fields and (if its
/// completion gates pass) advance <c>current_step_id</c> to the next step.
/// Idempotent on the field-application side; advancing twice from the same
/// step is a no-op once the pointer has moved.
///
/// Materialize-on-first-patch: when <see cref="WorkflowRun.EntityId"/> is null
/// (the run was started but the entity row hasn't been created yet), the
/// patch must target the workflow's first step. We merge the run's stashed
/// <see cref="WorkflowRun.DraftPayload"/> with the incoming field payload,
/// call the registered <see cref="IWorkflowEntityCreator"/> to materialize the
/// row, stamp the new id back onto the run, then continue with the standard
/// apply + gate-check + advance flow. Patches against any other step while
/// the entity is still null return 409 — those steps cannot meaningfully
/// run before the primary entity exists.
/// </summary>
public record PatchWorkflowStepCommand(int RunId, PatchWorkflowStepRequestModel Body)
    : IRequest<WorkflowRunResponseModel>;

public class PatchWorkflowStepValidator : AbstractValidator<PatchWorkflowStepCommand>
{
    public PatchWorkflowStepValidator()
    {
        RuleFor(x => x.Body.StepId).NotEmpty().MaximumLength(64);
    }
}

public class PatchWorkflowStepHandler(
    AppDbContext db,
    IEnumerable<IWorkflowEntityCreator> creators,
    IEnumerable<IWorkflowFieldApplier> appliers,
    IEntityReadinessService readiness,
    ISystemAuditWriter auditWriter,
    IClock clock) : IRequestHandler<PatchWorkflowStepCommand, WorkflowRunResponseModel>
{
    private readonly Dictionary<string, IWorkflowEntityCreator> _creators =
        creators.ToDictionary(c => c.EntityType, StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IWorkflowFieldApplier> _appliers =
        appliers.ToDictionary(a => a.EntityType, StringComparer.OrdinalIgnoreCase);

    public async Task<WorkflowRunResponseModel> Handle(PatchWorkflowStepCommand request, CancellationToken ct)
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
        var stepIndex = WorkflowStepHelper.IndexOf(steps, request.Body.StepId);
        if (stepIndex < 0)
            throw new InvalidOperationException(
                $"Step '{request.Body.StepId}' is not part of definition '{def.DefinitionId}'.");
        var step = steps[stepIndex];

        // Materialize-on-first-patch — if the entity hasn't been created yet,
        // the patch must target the materialization step (always the first
        // step in the definition). We merge the stashed initial payload with
        // the incoming fields, call the creator, then fall through to the
        // standard apply + gates flow against the new entity id.
        var materialized = false;
        if (run.EntityId is null)
        {
            if (stepIndex != 0)
                throw new InvalidOperationException(
                    $"Cannot patch step '{step.Id}' before the workflow's first step has materialized the entity.");

            if (!_creators.TryGetValue(run.EntityType, out var creator))
                throw new InvalidOperationException(
                    $"No workflow entity creator registered for entity type '{run.EntityType}'.");

            using var merged = MergePayloads(run.DraftPayload, request.Body.Fields);
            var newId = await creator.CreateDraftAsync(merged.RootElement, ct);

            run.EntityId = newId;
            run.DraftPayload = null;

            // Insert the junction row now that the entity exists. The composite
            // PK (RunId, EntityType, EntityId) requires non-null EntityId, so
            // this could not happen at workflow start.
            db.WorkflowRunEntities.Add(new WorkflowRunEntity
            {
                Run = run,
                EntityType = run.EntityType,
                EntityId = newId,
                Role = "primary",
            });
            materialized = true;
        }

        // Apply the field payload via the entity-type adapter. After
        // materialization the same payload also flows through ApplyAsync so
        // any per-applier normalization (trims, defaults, computed fields)
        // runs uniformly with the post-materialization path.
        if (!_appliers.TryGetValue(run.EntityType, out var applier))
            throw new InvalidOperationException(
                $"No workflow field applier registered for entity type '{run.EntityType}'.");
        await applier.ApplyAsync(run.EntityId.Value, request.Body.Fields, ct);

        // Re-evaluate the step's gates. If all required gates pass, advance.
        var advanced = false;
        if (step.CompletionGates.Count == 0 || await GatesPassAsync(run.EntityType, run.EntityId.Value, step.CompletionGates, ct))
        {
            // Move pointer forward IF we're at the current step. If the user
            // re-edited an earlier completed step, leave current_step_id where
            // it is (D2: clicking back to an earlier step doesn't reset).
            if (string.Equals(run.CurrentStepId, step.Id, StringComparison.OrdinalIgnoreCase))
            {
                run.CurrentStepId = stepIndex + 1 < steps.Count ? steps[stepIndex + 1].Id : run.CurrentStepId;
                advanced = true;
            }
        }

        run.LastActivityAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(
            action: WorkflowAuditEvents.StepAdvanced,
            userId: db.CurrentUserId ?? 0,
            entityType: WorkflowAuditEvents.EntityType,
            entityId: run.Id,
            details: JsonSerializer.Serialize(new
            {
                runId = run.Id,
                stepId = step.Id,
                advanced,
                materialized,
                entityId = run.EntityId,
                currentStepId = run.CurrentStepId,
            }),
            ct: ct);

        return run.ToResponse();
    }

    /// <summary>
    /// Merge the workflow run's stashed initial payload (raw JSON, possibly
    /// null) with the incoming step-field payload. Step fields win on key
    /// collision since they represent the user's most recent edit.
    /// </summary>
    private static JsonDocument MergePayloads(string? draftPayload, JsonElement fields)
    {
        var buffer = new System.IO.MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            var written = new HashSet<string>(StringComparer.Ordinal);

            if (fields.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in fields.EnumerateObject())
                {
                    prop.WriteTo(writer);
                    written.Add(prop.Name);
                }
            }

            if (!string.IsNullOrWhiteSpace(draftPayload))
            {
                using var draft = JsonDocument.Parse(draftPayload);
                if (draft.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in draft.RootElement.EnumerateObject())
                    {
                        if (written.Contains(prop.Name)) continue;
                        prop.WriteTo(writer);
                    }
                }
            }
            writer.WriteEndObject();
        }
        buffer.Position = 0;
        return JsonDocument.Parse(buffer);
    }

    private async Task<bool> GatesPassAsync(
        string entityType, int entityId,
        IReadOnlyList<string> gateIds, CancellationToken ct)
    {
        var missing = await readiness.GetMissingValidatorsAsync(entityType, entityId, ct);
        if (missing.Count == 0) return true;
        // gates pass if NONE of the gateIds are in missing.ValidatorId set.
        var failing = missing.Select(m => m.ValidatorId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var gate in gateIds)
            if (failing.Contains(gate)) return false;
        return true;
    }
}
