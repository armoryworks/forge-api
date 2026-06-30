using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Api.Features.Events;

public record DeleteEventCommand(int Id) : IRequest;

public class DeleteEventHandler(AppDbContext db)
    : IRequestHandler<DeleteEventCommand>
{
    public async Task Handle(DeleteEventCommand request, CancellationToken cancellationToken)
    {
        // Load attendees once so we can notify them of the cancellation.
        var evt = await db.Events
            .Include(e => e.Attendees)
            .FirstOrDefaultAsync(e => e.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Event {request.Id} not found");

        // Soft cancel rather than hard delete
        evt.IsCancelled = true;

        // Notify each attendee the event was cancelled (excluding the actor who
        // cancelled it, when known). Mirrors the events notification pattern used
        // by CreateEvent / EventReminderJob — direct db.Notifications.Add, no SignalR.
        var actorUserId = db.CurrentUserId;
        foreach (var attendee in evt.Attendees.Where(a => a.UserId != actorUserId))
        {
            db.Notifications.Add(new Notification
            {
                UserId = attendee.UserId,
                Type = "event_cancelled",
                Severity = "warning",
                Source = "events",
                Title = $"Cancelled: {evt.Title}",
                Message = $"\"{evt.Title}\" on {evt.StartTime:MM/dd/yyyy hh:mm tt} has been cancelled.",
                EntityType = "events",
                EntityId = evt.Id,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
