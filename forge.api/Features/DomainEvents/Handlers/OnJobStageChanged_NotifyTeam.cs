using MediatR;

using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Api.Features.DomainEvents.Handlers;

public class OnJobStageChanged_NotifyTeam(
    AppDbContext db,
    IHubContext<NotificationHub> notificationHub,
    ILogger<OnJobStageChanged_NotifyTeam> logger)
    : INotificationHandler<JobStageChangedEvent>
{
    public async Task Handle(JobStageChangedEvent notification, CancellationToken ct)
    {
        var job = await db.Jobs
            .Include(j => j.CurrentStage)
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == notification.JobId, ct);

        if (job is null) return;

        var toStage = await db.JobStages
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == notification.ToStageId, ct);

        if (toStage is null) return;

        // Only notify when job enters QC stage
        if (!toStage.Name.Contains("QC", StringComparison.OrdinalIgnoreCase) &&
            !toStage.Name.Contains("Review", StringComparison.OrdinalIgnoreCase))
            return;

        var recipientIds = new HashSet<int>();

        // Notify the job's assignee
        if (job.AssigneeId.HasValue)
            recipientIds.Add(job.AssigneeId.Value);

        // Notify production leads (Manager/Admin)
        var leaderIds = await db.UserRoles
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
            .Where(x => x.Name == "Manager" || x.Name == "Admin")
            .Select(x => x.UserId)
            .ToListAsync(ct);

        foreach (var id in leaderIds)
            recipientIds.Add(id);

        // Don't notify the user who moved the job
        recipientIds.Remove(notification.UserId);

        foreach (var userId in recipientIds)
        {
            db.Notifications.Add(new Notification
            {
                UserId = userId,
                Type = "job_entered_qc",
                Severity = "info",
                Source = "kanban",
                Title = "Job Entered QC",
                Message = $"Job {job.JobNumber} — {job.Title} has entered {toStage.Name}.",
                EntityType = "Job",
                EntityId = job.Id,
                SenderId = notification.UserId,
            });
        }

        await db.SaveChangesAsync(ct);

        foreach (var userId in recipientIds)
        {
            await notificationHub.Clients.Group($"user:{userId}")
                .SendAsync("notificationReceived", new { type = "job_entered_qc", jobId = job.Id }, ct);
        }

        logger.LogInformation("Notified {Count} user(s) about job {JobNumber} entering QC", recipientIds.Count, job.JobNumber);
    }
}
