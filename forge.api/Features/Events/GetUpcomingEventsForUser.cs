using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Events;

public record GetUpcomingEventsForUserQuery(int UserId) : IRequest<List<EventResponseModel>>;

public class GetUpcomingEventsForUserHandler(AppDbContext db)
    : IRequestHandler<GetUpcomingEventsForUserQuery, List<EventResponseModel>>
{
    public async Task<List<EventResponseModel>> Handle(
        GetUpcomingEventsForUserQuery request, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        var eventIds = await db.EventAttendees
            .Where(a => a.UserId == request.UserId)
            .Select(a => a.EventId)
            .ToListAsync(cancellationToken);

        var events = await db.Events
            .Include(e => e.Attendees)
            .Include(e => e.CalendarEventType)
            .Where(e => eventIds.Contains(e.Id)
                && !e.IsCancelled
                && e.StartTime >= now)
            .OrderBy(e => e.StartTime)
            .Take(10)
            .ToListAsync(cancellationToken);

        var userIds = events
            .SelectMany(e => e.Attendees.Select(a => a.UserId))
            .Concat(events.Select(e => e.CreatedByUserId))
            .Distinct()
            .ToList();

        var userNames = await db.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.LastName + ", " + u.FirstName, cancellationToken);

        return events.Select(evt => new EventResponseModel(
            evt.Id, evt.Title, evt.Description, evt.StartTime, evt.EndTime,
            evt.Location, evt.EventType.ToString(), evt.IsRequired, evt.IsCancelled,
            evt.CreatedByUserId,
            userNames.GetValueOrDefault(evt.CreatedByUserId, ""),
            evt.Attendees.Select(a => new EventAttendeeResponseModel(
                a.Id, a.UserId,
                userNames.GetValueOrDefault(a.UserId, ""),
                a.Status.ToString(), a.RespondedAt)).ToList(),
            evt.CreatedAt, evt.EventTypeId, evt.CalendarEventType?.SuperGroupId)).ToList();
    }
}
