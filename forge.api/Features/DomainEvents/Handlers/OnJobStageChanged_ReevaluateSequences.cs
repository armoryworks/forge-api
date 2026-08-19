using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Sequences;
using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Features.DomainEvents.Handlers;

/// <summary>Gated Sequence Engine reaction: a job moved stage → re-evaluate its running sequences (job-stage gates and anything else that reads the job).</summary>
public class OnJobStageChanged_ReevaluateSequences(AppDbContext db, IMediator mediator) : INotificationHandler<JobStageChangedEvent>
{
    public async Task Handle(JobStageChangedEvent notification, CancellationToken cancellationToken)
    {
        var ids = await db.SequenceInstances
            .Where(i => i.Status == SequenceInstanceStatus.Running && i.DeletedAt == null
                        && i.SubjectEntityType == "Job" && i.SubjectEntityId == notification.JobId)
            .Select(i => i.Id).ToListAsync(cancellationToken);
        foreach (var id in ids)
            await mediator.Send(new ReevaluateSequenceCommand(id, notification.UserId), cancellationToken);
    }
}
