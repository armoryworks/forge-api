using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Api.Features.DomainEvents.Handlers;

public class OnJobCreated_CreateDueDateEvent(
    AppDbContext db,
    ILogger<OnJobCreated_CreateDueDateEvent> logger)
    : INotificationHandler<JobCreatedEvent>
{
    public async Task Handle(JobCreatedEvent notification, CancellationToken ct)
    {
        var job = await db.Jobs
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == notification.JobId, ct);

        if (job is null)
        {
            logger.LogWarning("Job {Id} not found for due date event creation", notification.JobId);
            return;
        }

        if (!job.DueDate.HasValue)
        {
            logger.LogInformation("Job {JobNumber} has no due date — skipping calendar event creation", job.JobNumber);
            return;
        }

        var title = $"Job Due: {job.JobNumber} - {job.Title}";

        db.Events.Add(new Event
        {
            Title = title.Length > 200 ? title[..200] : title,
            Description = $"Due date for Job {job.JobNumber}: {job.Title}.",
            StartTime = job.DueDate.Value,
            EndTime = job.DueDate.Value,
            EventType = EventType.Other,
            IsAllDay = true,
            IsSystemGenerated = true,
            CreatedByUserId = notification.UserId,
        });

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Created due date calendar event for Job {JobNumber}", job.JobNumber);
    }
}
