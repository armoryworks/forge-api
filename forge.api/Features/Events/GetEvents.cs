using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Events;

public record GetEventsQuery(DateTimeOffset? From, DateTimeOffset? To, string? EventType)
    : IRequest<List<EventResponseModel>>;

public class GetEventsHandler(AppDbContext db, ICalendarVisibilityService visibility)
    : IRequestHandler<GetEventsQuery, List<EventResponseModel>>
{
    public async Task<List<EventResponseModel>> Handle(
        GetEventsQuery request, CancellationToken cancellationToken)
    {
        var query = db.Events
            .Include(e => e.Attendees)
            .Include(e => e.CalendarEventType)
            .Where(e => !e.IsCancelled)
            .AsQueryable();

        if (request.From.HasValue)
            query = query.Where(e => e.StartTime >= request.From.Value);

        if (request.To.HasValue)
            query = query.Where(e => e.StartTime <= request.To.Value);

        if (!string.IsNullOrEmpty(request.EventType))
            query = query.Where(e => e.EventType.ToString() == request.EventType);

        // compliance-calendar A-2: restrict to Super-Groups the current user may see.
        // null = unrestricted (Admin/system). Events without a type (expand-phase) stay visible.
        var visibleGroupIds = await visibility.GetVisibleSuperGroupIdsAsync(cancellationToken);
        if (visibleGroupIds is not null)
            query = query.Where(e => e.EventTypeId == null
                || db.CalendarEventTypes.Any(t => t.Id == e.EventTypeId && visibleGroupIds.Contains(t.SuperGroupId)));

        var events = await query
            .OrderBy(e => e.StartTime)
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
