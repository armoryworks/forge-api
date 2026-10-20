using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Data;
using Forge.Api.Features.Terminology;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Features.DomainEvents.Handlers;

public class OnJobCreated_CreateDueDateEvent(
    AppDbContext db,
    ITerminologyLabels terminology,
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

        var eventTypeId = await db.CalendarEventTypes
            .Where(t => t.Key == SeedData.JobDueEventTypeKey)
            .Select(t => (int?)t.Id)
            .FirstOrDefaultAsync(ct);

        var jobTerm = (await terminology.GetAsync(ct)).Singular("job");
        var title = $"{jobTerm} Due: {job.JobNumber} - {job.Title}";

        db.Events.Add(new Event
        {
            Title = title.Length > 200 ? title[..200] : title,
            Description = $"Due date for {jobTerm} {job.JobNumber}: {job.Title}.",
            StartTime = job.DueDate.Value,
            EndTime = job.DueDate.Value,
            EventType = EventType.Other,
            EventTypeId = eventTypeId,
            IsAllDay = true,
            IsSystemGenerated = true,
            CreatedByUserId = notification.UserId,
        });

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Created due date calendar event for Job {JobNumber}", job.JobNumber);
    }
}
