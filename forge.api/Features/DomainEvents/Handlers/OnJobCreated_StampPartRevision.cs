using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Features.DomainEvents.Handlers;

public class OnJobCreated_StampPartRevision(AppDbContext db) : INotificationHandler<JobCreatedEvent>
{
    public async Task Handle(JobCreatedEvent notification, CancellationToken cancellationToken)
    {
        var job = await db.Jobs
            .Include(j => j.Part)
            .FirstOrDefaultAsync(j => j.Id == notification.JobId, cancellationToken);
        if (job?.Part is null || job.PartRevision is not null)
            return;

        job.PartRevision = job.Part.Revision;
        db.JobActivityLogs.Add(new JobActivityLog
        {
            JobId = job.Id,
            UserId = notification.UserId > 0 ? notification.UserId : null,
            Action = ActivityAction.FieldChanged,
            FieldName = "PartRevision",
            NewValue = job.PartRevision,
            Description = $"Made to {job.Part.PartNumber} rev {job.PartRevision}.",
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
