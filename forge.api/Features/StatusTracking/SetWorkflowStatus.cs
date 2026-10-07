using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Api.Hubs;
using Forge.Api.Middleware;
using Forge.Api.Services;

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
    ICapabilitySnapshotProvider capabilities,
    ISystemAuditWriter auditWriter,
    IClock clock)
    : IRequestHandler<SetWorkflowStatusCommand, StatusEntryResponseModel>
{
    public const string JobArchivedStatusCode = "job_status_archived";

    private const string WorkflowStatusField = "WorkflowStatus";
    private const string JobArchiveCapability = "CAP-MFG-WO-RELEASE";
    private static readonly string[] JobArchiveRoles =
        ["Admin", "Manager", "PM", "Engineer", "ProductionWorker", "OfficeManager"];

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

        var isJob = string.Equals(request.EntityType, "job", System.StringComparison.OrdinalIgnoreCase);
        var archiveToggledJob = isJob
            ? await FindJobToToggleArchiveAsync(
                request.EntityId, request.Data.StatusCode == JobArchivedStatusCode, wasArchivedStatus, cancellationToken)
            : null;

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
        if (isJob)
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

        if (isJob)
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

            if (archiveToggledJob is not null)
                await ToggleJobArchivedAsync(archiveToggledJob, currentUserId, cancellationToken);
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
            if (!archiveToggledJob.IsArchived)
            {
                await auditWriter.WriteAsync(
                    "JobUnarchived",
                    db.CurrentUserId ?? 0,
                    entityType: "Job",
                    entityId: archiveToggledJob.Id,
                    ct: cancellationToken);
            }

            await boardHub.Clients.Group($"board:{archiveToggledJob.TrackTypeId}")
                .SendAsync("boardUpdated",
                    new { reason = archiveToggledJob.IsArchived ? "archive" : "unarchive" },
                    cancellationToken);
        }

        // Reload to return with SetBy info
        var history = await repository.GetHistoryAsync(request.EntityType, request.EntityId, cancellationToken);
        return history.First(h => h.Id == statusEntry.Id);
    }

    private async Task<Job?> FindJobToToggleArchiveAsync(
        int jobId, bool archive, bool wasArchivedStatus, CancellationToken cancellationToken)
    {
        if (!archive && !wasArchivedStatus)
            return null;

        var job = await db.Jobs.FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (job is null || job.IsArchived == archive)
            return null;

        var user = httpContext.HttpContext?.User;

        if (archive)
        {
            if (!JobArchiveRoles.Any(role => user?.IsInRole(role) == true))
                throw new ForbiddenException("You do not have permission to archive jobs.");
            if (!capabilities.IsEnabled(JobArchiveCapability))
                throw new CapabilityDisabledException(JobArchiveCapability);
            return job;
        }

        var lastArchivedBy = await db.JobActivityLogs
            .Where(l => l.JobId == jobId && l.Action == ActivityAction.Archived)
            .OrderByDescending(l => l.Id)
            .Select(l => l.FieldName)
            .FirstOrDefaultAsync(cancellationToken);
        if (lastArchivedBy != WorkflowStatusField)
            return null;

        if (user?.IsInRole("Admin") != true)
            throw new ForbiddenException("Only an administrator can restore an archived job.");

        return job;
    }

    private async Task ToggleJobArchivedAsync(Job job, int? userId, CancellationToken cancellationToken)
    {
        job.IsArchived = !job.IsArchived;

        await activityRepo.AddAsync(new JobActivityLog
        {
            JobId = job.Id,
            UserId = userId,
            Action = job.IsArchived ? ActivityAction.Archived : ActivityAction.Restored,
            FieldName = WorkflowStatusField,
            Description = job.IsArchived ? "Archived (workflow status)." : "Unarchived (workflow status).",
        }, cancellationToken);
    }
}
