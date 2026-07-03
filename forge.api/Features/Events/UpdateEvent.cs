using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Events;

public record UpdateEventCommand(
    int Id,
    string Title,
    string? Description,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    string? Location,
    string EventType,
    bool IsRequired,
    List<int> AttendeeUserIds,
    int? EventTypeId = null) : IRequest<EventResponseModel>;

public class UpdateEventValidator : AbstractValidator<UpdateEventCommand>
{
    public UpdateEventValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.Location).MaximumLength(200);
        RuleFor(x => x.EventType).NotEmpty().Must(t => Enum.TryParse<EventType>(t, true, out _));
        RuleFor(x => x.EndTime).GreaterThan(x => x.StartTime);
    }
}

public class UpdateEventHandler(AppDbContext db)
    : IRequestHandler<UpdateEventCommand, EventResponseModel>
{
    public async Task<EventResponseModel> Handle(
        UpdateEventCommand request, CancellationToken cancellationToken)
    {
        var evt = await db.Events
            .Include(e => e.Attendees)
            .Include(e => e.CalendarEventType)
            .FirstOrDefaultAsync(e => e.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Event {request.Id} not found");

        evt.Title = request.Title;
        evt.Description = request.Description;
        evt.StartTime = request.StartTime;
        evt.EndTime = request.EndTime;
        evt.Location = request.Location;
        evt.EventType = Enum.Parse<EventType>(request.EventType, true);
        if (request.EventTypeId is int typeId
            && !await db.CalendarEventTypes.AnyAsync(t => t.Id == typeId, cancellationToken))
            throw new KeyNotFoundException($"Calendar event type {typeId} not found");
        evt.EventTypeId = request.EventTypeId;
        evt.IsRequired = request.IsRequired;

        // Sync attendees
        var existingUserIds = evt.Attendees.Select(a => a.UserId).ToHashSet();
        var requestedUserIds = request.AttendeeUserIds.Distinct().ToHashSet();

        // Remove attendees not in request
        var toRemove = evt.Attendees.Where(a => !requestedUserIds.Contains(a.UserId)).ToList();
        foreach (var attendee in toRemove)
            evt.Attendees.Remove(attendee);

        // Add new attendees
        foreach (var userId in requestedUserIds.Except(existingUserIds))
        {
            evt.Attendees.Add(new EventAttendee
            {
                UserId = userId,
                Status = AttendeeStatus.Invited,
            });
        }

        await db.SaveChangesAsync(cancellationToken);

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
            evt.CreatedAt, evt.EventTypeId, evt.CalendarEventType?.SuperGroupId,
            evt.Status.ToString(), evt.OwnerUserId, evt.IsBlocking, evt.AcknowledgedAt);
    }
}
