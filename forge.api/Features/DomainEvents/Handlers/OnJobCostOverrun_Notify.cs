using System.Globalization;

using MediatR;

using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Api.Features.DomainEvents.Handlers;

public class OnJobCostOverrun_Notify(
    AppDbContext db,
    IHubContext<NotificationHub> notificationHub,
    ILogger<OnJobCostOverrun_Notify> logger)
    : INotificationHandler<JobCostOverrunEvent>
{
    internal const string NotificationType = "job_cost_overrun";
    private static readonly string[] RecipientRoles = ["Manager", "PM"];

    public async Task Handle(JobCostOverrunEvent notification, CancellationToken ct)
    {
        var job = await db.Jobs
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == notification.JobId, ct);

        if (job is null) return;

        var recipients = await AlertRecipients.ActiveUserIdsInRolesAsync(db, RecipientRoles, ct);
        if (job.AssigneeId is { } assigneeId && !recipients.Contains(assigneeId)
            && await db.Users.AnyAsync(u => u.Id == assigneeId && u.IsActive, ct))
        {
            recipients.Add(assigneeId);
        }

        var alreadyAlerted = await AlertRecipients.UsersWithOpenAlertAsync(db, NotificationType, "Job", job.Id, ct);
        var newRecipients = recipients.Where(id => !alreadyAlerted.Contains(id)).ToList();

        if (newRecipients.Count == 0) return;

        var overPercent = Math.Round(notification.VariancePercent * 100m, 0, MidpointRounding.AwayFromZero);
        var message = string.Create(CultureInfo.InvariantCulture,
            $"{job.JobNumber} {job.Title}: actual cost {notification.ActualCost:N2} against an estimate of {notification.EstimatedCost:N2} ({overPercent:0}% overrun).");

        foreach (var userId in newRecipients)
        {
            db.Notifications.Add(new Notification
            {
                UserId = userId,
                Type = NotificationType,
                Severity = "warning",
                Source = "jobs",
                Title = $"Cost overrun: {job.JobNumber}",
                Message = message,
                EntityType = "Job",
                EntityId = job.Id,
            });
        }

        await db.SaveChangesAsync(ct);

        foreach (var userId in newRecipients)
        {
            await notificationHub.Clients.Group($"user:{userId}")
                .SendAsync("notificationReceived", new { type = NotificationType, jobId = job.Id }, ct);
        }

        logger.LogInformation("Notified {Count} user(s) about a cost overrun on job {JobNumber}",
            newRecipients.Count, job.JobNumber);
    }
}
