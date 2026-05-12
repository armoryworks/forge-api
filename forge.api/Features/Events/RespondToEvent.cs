using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Features.Events;

public record RespondToEventCommand(int EventId, int UserId, string Status) : IRequest;

public class RespondToEventHandler(AppDbContext db)
    : IRequestHandler<RespondToEventCommand>
{
    public async Task Handle(RespondToEventCommand request, CancellationToken cancellationToken)
    {
        var attendee = await db.EventAttendees
            .FirstOrDefaultAsync(a => a.EventId == request.EventId && a.UserId == request.UserId, cancellationToken)
            ?? throw new KeyNotFoundException($"Attendee not found for event {request.EventId} and user {request.UserId}");

        if (!Enum.TryParse<AttendeeStatus>(request.Status, true, out var status))
            throw new ArgumentException($"Invalid status: {request.Status}");

        attendee.Status = status;
        attendee.RespondedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }
}
