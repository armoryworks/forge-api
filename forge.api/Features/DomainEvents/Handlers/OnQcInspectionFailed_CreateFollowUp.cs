using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.DomainEvents.Handlers;

public class OnQcInspectionFailed_CreateFollowUp(
    AppDbContext db,
    IClock clock,
    ILogger<OnQcInspectionFailed_CreateFollowUp> logger)
    : INotificationHandler<QcInspectionFailedEvent>
{
    public async Task Handle(QcInspectionFailedEvent notification, CancellationToken ct)
    {
        var job = await db.Jobs
            .AsNoTracking()
            .Where(j => j.Id == notification.JobId)
            .Select(j => new { j.Id, j.JobNumber, j.AssigneeId })
            .FirstOrDefaultAsync(ct);

        if (job is null) return;

        // Check for existing open follow-up for this job's QC failure
        var exists = await db.FollowUpTasks
            .AnyAsync(f =>
                f.SourceEntityType == "Job" &&
                f.SourceEntityId == notification.JobId &&
                f.TriggerType == FollowUpTriggerType.QcFailure &&
                f.Status == FollowUpStatus.Open, ct);

        if (exists)
        {
            logger.LogDebug("QC failure follow-up already exists for job {JobId}", notification.JobId);
            return;
        }

        // Assign to job's assignee, or fall back to first Manager/Admin
        var assigneeId = job.AssigneeId ?? 0;
        if (assigneeId == 0)
        {
            assigneeId = await db.UserRoles
                .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
                .Where(x => x.Name == "Manager" || x.Name == "Admin")
                .Select(x => x.UserId)
                .FirstOrDefaultAsync(ct);
        }

        if (assigneeId == 0) return;

        db.FollowUpTasks.Add(new FollowUpTask
        {
            Title = $"QC Failed: {job.JobNumber} — Review required",
            Description = $"QC inspection #{notification.InspectionId} failed for job {job.JobNumber}. Review the inspection results and determine corrective action.",
            AssignedToUserId = assigneeId,
            DueDate = clock.UtcNow.AddDays(1),
            SourceEntityType = "Job",
            SourceEntityId = notification.JobId,
            TriggerType = FollowUpTriggerType.QcFailure,
            Status = FollowUpStatus.Open,
        });

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Created QC failure follow-up for job {JobNumber} (inspection {InspectionId})",
            job.JobNumber, notification.InspectionId);
    }
}
