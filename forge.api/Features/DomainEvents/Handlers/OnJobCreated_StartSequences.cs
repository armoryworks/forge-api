using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Sequences;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.DomainEvents.Handlers;

/// <summary>
/// Gated Sequence Engine reaction: a new job starts a run of every Published definition flagged
/// AutoStartOnSubjectCreate for subject type "Job" (latest published version per code). Idempotent per (job, code):
/// a job that already has a running instance of that code is left alone.
/// </summary>
public class OnJobCreated_StartSequences(AppDbContext db, IMediator mediator) : INotificationHandler<JobCreatedEvent>
{
    public async Task Handle(JobCreatedEvent notification, CancellationToken cancellationToken)
    {
        var defs = await db.SequenceDefinitions
            .Where(d => d.DeletedAt == null && d.Status == SequenceDefinitionStatus.Published
                        && d.SubjectEntityType == "Job" && d.AutoStartOnSubjectCreate)
            .OrderByDescending(d => d.Version)
            .ToListAsync(cancellationToken);
        if (defs.Count == 0) return;

        var running = await db.SequenceInstances
            .Where(i => i.SubjectEntityType == "Job" && i.SubjectEntityId == notification.JobId && i.Status == SequenceInstanceStatus.Running)
            .Select(i => i.Definition!.Code).ToListAsync(cancellationToken);

        foreach (var def in defs.GroupBy(d => d.Code).Select(g => g.First()))
        {
            if (running.Contains(def.Code)) continue;
            await mediator.Send(new StartSequenceInstanceCommand(
                new StartSequenceRequestModel(def.Id, null, "Job", notification.JobId), notification.UserId), cancellationToken);
        }
    }
}
