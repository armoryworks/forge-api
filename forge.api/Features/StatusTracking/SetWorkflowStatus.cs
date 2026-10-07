using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Hubs;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.StatusTracking;

public record SetWorkflowStatusCommand(
    string EntityType,
    int EntityId,
    SetStatusRequestModel Data) : IRequest<StatusEntryResponseModel>;

public class SetWorkflowStatusCommandValidator : AbstractValidator<SetWorkflowStatusCommand>
{
    public SetWorkflowStatusCommandValidator()
    {
        RuleFor(x => x.EntityType).NotEmpty().MaximumLength(50);
        RuleFor(x => x.EntityId).GreaterThan(0);
        RuleFor(x => x.Data.StatusCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Data.Notes).MaximumLength(2000).When(x => x.Data.Notes is not null);
    }
}

public class SetWorkflowStatusHandler(
    AppDbContext db,
    IStatusEntryRepository repository,
    IActivityLogRepository activityRepo,
    IWorkCenterContext workCenterContext,
    IHttpContextAccessor httpContext,
    IHubContext<BoardHub> boardHub,
    IClock clock)
    : IRequestHandler<SetWorkflowStatusCommand, StatusEntryResponseModel>
{
    public const string JobArchivedStatusCode = "job_status_archived";

    public async Task<StatusEntryResponseModel> Handle(
        SetWorkflowStatusCommand request, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        // Close any existing active workflow status
        var currentWorkflow = await db.StatusEntries
            .Where(s => s.EntityType == request.EntityType
                        && s.EntityId == request.EntityId
                        && s.Category == "workflow"
                        && s.EndedAt == null)
            .ToListAsync(cancellationToken);

        var previousLabel = currentWorkflow.FirstOrDefault()?.StatusLabel;
        var wasArchivedStatus = currentWorkflow.Any(e => e.StatusCode == JobArchivedStatusCode);

        foreach (var entry in currentWorkflow)
        {
            entry.EndedAt = now;
        }

        // Resolve label from reference_data (fallback to StatusCode if not found)
        var label = await db.ReferenceData
            .Where(r => r.Code == request.Data.StatusCode && r.IsActive)
            .Select(r => r.Label)
            .FirstOrDefaultAsync(cancellationToken) ?? request.Data.StatusCode;

        var userIdClaim = httpContext.HttpContext?.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
        int? currentUserId = userIdClaim is not null ? int.Parse(userIdClaim.Value) : null;

        // Capture work-center context only for jobs.
        int? workCenterId = null;
        int? operationId = null;
        if (string.Equals(request.EntityType, "job", System.StringComparison.OrdinalIgnoreCase))
        {
            (workCenterId, operationId) = await workCenterContext.ResolveForJobAsync(
                request.EntityId, currentUserId, cancellationToken);
        }

        var statusEntry = new StatusEntry
        {
            EntityType = request.EntityType,
            EntityId = request.EntityId,
            StatusCode = request.Data.StatusCode,
            StatusLabel = label,
            Category = "workflow",
            StartedAt = now,
            EndedAt = null,
            Notes = request.Data.Notes?.Trim(),
            WorkCenterId = workCenterId,
            OperationId = operationId,
        };

        await db.StatusEntries.AddAsync(statusEntry, cancellationToken);

        var description = previousLabel is not null
            ? $"Status changed from {previousLabel} to {label}."
            : $"Status set to {label}.";

        Job? archiveToggledJob = null;
        if (string.Equals(request.EntityType, "job", System.StringComparison.OrdinalIgnoreCase))
        {
            await activityRepo.AddAsync(new JobActivityLog
            {
                JobId = request.EntityId,
                UserId = currentUserId,
                Action = ActivityAction.StatusChanged,
                FieldName = "WorkflowStatus",
                OldValue = previousLabel,
                NewValue = label,
                Description = description,
                WorkCenterId = workCenterId,
                OperationId = operationId,
            }, cancellationToken);

            archiveToggledJob = await SyncJobArchivedFlagAsync(
                request.EntityId, request.Data.StatusCode == JobArchivedStatusCode, wasArchivedStatus,
                currentUserId, cancellationToken);
        }
        else
        {
            await activityRepo.AddAsync(new ActivityLog
            {
                EntityType = request.EntityType,
                EntityId = request.EntityId,
                UserId = currentUserId,
                Action = ActivityAction.StatusChanged.ToString(),
                FieldName = "WorkflowStatus",
                OldValue = previousLabel,
                NewValue = label,
                Description = description,
            }, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);

        if (archiveToggledJob is not null)
        {
            await boardHub.Clients.Group($"board:{archiveToggledJob.TrackTypeId}")
                .SendAsync("boardUpdated",
                    new { reason = archiveToggledJob.IsArchived ? "archive" : "unarchive" },
                    cancellationToken);
        }

        // Reload to return with SetBy info
        var history = await repository.GetHistoryAsync(request.EntityType, request.EntityId, cancellationToken);
        return history.First(h => h.Id == statusEntry.Id);
    }

    private async Task<Job?> SyncJobArchivedFlagAsync(
        int jobId, bool archive, bool wasArchivedStatus, int? userId, CancellationToken cancellationToken)
    {
        if (!archive && !wasArchivedStatus)
            return null;

        var job = await db.Jobs.FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (job is null || job.IsArchived == archive)
            return null;

        job.IsArchived = archive;

        await activityRepo.AddAsync(new JobActivityLog
        {
            JobId = job.Id,
            UserId = userId,
            Action = archive ? ActivityAction.Archived : ActivityAction.Restored,
            Description = archive ? "Archived (workflow status)." : "Unarchived (workflow status).",
        }, cancellationToken);

        return job;
    }
}
