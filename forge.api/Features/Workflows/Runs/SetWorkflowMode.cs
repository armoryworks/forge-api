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
/// Workflow Pattern Phase 3 / D4 — Toggle express ↔ guided mid-flow.
/// Available at any point in an active run.
/// </summary>
public record SetWorkflowModeCommand(int RunId, SetWorkflowModeRequestModel Body)
    : IRequest<WorkflowRunResponseModel>;

public class SetWorkflowModeValidator : AbstractValidator<SetWorkflowModeCommand>
{
    public SetWorkflowModeValidator()
    {
        RuleFor(x => x.Body.Mode)
            .NotEmpty()
            .Must(m => m == "express" || m == "guided")
            .WithMessage("mode must be 'express' or 'guided'.");
    }
}

public class SetWorkflowModeHandler(
    AppDbContext db,
    ISystemAuditWriter auditWriter,
    IClock clock) : IRequestHandler<SetWorkflowModeCommand, WorkflowRunResponseModel>
{
    public async Task<WorkflowRunResponseModel> Handle(SetWorkflowModeCommand request, CancellationToken ct)
    {
        var run = await db.WorkflowRuns.FirstOrDefaultAsync(r => r.Id == request.RunId, ct)
            ?? throw new KeyNotFoundException($"Workflow run id {request.RunId} not found.");
        if (run.CompletedAt is not null || run.AbandonedAt is not null)
            throw new InvalidOperationException("Workflow run is no longer active.");

        var from = run.Mode;
        run.Mode = request.Body.Mode;
        run.LastActivityAt = clock.UtcNow;

        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(
            action: WorkflowAuditEvents.ModeToggled,
            userId: db.CurrentUserId ?? 0,
            entityType: WorkflowAuditEvents.EntityType,
            entityId: run.Id,
            details: JsonSerializer.Serialize(new { runId = run.Id, from, to = run.Mode }),
            ct: ct);

        return run.ToResponse();
    }
}
