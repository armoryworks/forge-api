using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Events;

public record GetEventByIdQuery(int Id) : IRequest<EventResponseModel>;

public class GetEventByIdHandler(AppDbContext db)
    : IRequestHandler<GetEventByIdQuery, EventResponseModel>
{
    public async Task<EventResponseModel> Handle(
        GetEventByIdQuery request, CancellationToken cancellationToken)
    {
        var evt = await db.Events
            .Include(e => e.Attendees)
            .FirstOrDefaultAsync(e => e.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Event {request.Id} not found");

        var userIds = evt.Attendees.Select(a => a.UserId)
            .Append(evt.CreatedByUserId).Distinct().ToList();

        var userNames = await db.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.LastName + ", " + u.FirstName, cancellationToken);

        return new EventResponseModel(
            evt.Id, evt.Title, evt.Description, evt.StartTime, evt.EndTime,
            evt.Location, evt.EventType.ToString(), evt.IsRequired, evt.IsCancelled,
            evt.CreatedByUserId,
            userNames.GetValueOrDefault(evt.CreatedByUserId, ""),
            evt.Attendees.Select(a => new EventAttendeeResponseModel(
                a.Id, a.UserId,
                userNames.GetValueOrDefault(a.UserId, ""),
                a.Status.ToString(), a.RespondedAt)).ToList(),
            evt.CreatedAt);
    }
}
