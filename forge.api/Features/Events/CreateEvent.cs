using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Events;

public record CreateEventCommand(
    string Title,
    string? Description,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    string? Location,
    string EventType,
    bool IsRequired,
    List<int> AttendeeUserIds,
    int? EventTypeId = null) : IRequest<EventResponseModel>;

public class CreateEventValidator : AbstractValidator<CreateEventCommand>
{
    public CreateEventValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.Location).MaximumLength(200);
        RuleFor(x => x.EventType).NotEmpty().Must(t => Enum.TryParse<EventType>(t, true, out _))
            .WithMessage("Invalid event type");
        RuleFor(x => x.EndTime).GreaterThan(x => x.StartTime)
            .WithMessage("End time must be after start time");
    }
}

public class CreateEventHandler(AppDbContext db, IHttpContextAccessor httpContext)
    : IRequestHandler<CreateEventCommand, EventResponseModel>
{
    public async Task<EventResponseModel> Handle(CreateEventCommand request, CancellationToken cancellationToken)
    {
        var userId = int.Parse(httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // compliance-calendar A-1: validate the optional configurable type exists.
        if (request.EventTypeId is int typeId
            && !await db.CalendarEventTypes.AnyAsync(t => t.Id == typeId, cancellationToken))
            throw new KeyNotFoundException($"Calendar event type {typeId} not found");

        var evt = new Event
        {
            Title = request.Title,
            Description = request.Description,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            Location = request.Location,
            EventType = Enum.Parse<EventType>(request.EventType, true),
            EventTypeId = request.EventTypeId,
            IsRequired = request.IsRequired,
            CreatedByUserId = userId,
        };

        // Add attendees
        foreach (var attendeeUserId in request.AttendeeUserIds.Distinct())
        {
            evt.Attendees.Add(new EventAttendee
            {
                UserId = attendeeUserId,
                Status = AttendeeStatus.Invited,
            });
        }

        db.Events.Add(evt);
        await db.SaveChangesAsync(cancellationToken);

        // Create notifications for attendees
        var creatorName = await db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.LastName + ", " + u.FirstName)
            .FirstOrDefaultAsync(cancellationToken) ?? "Unknown";

        foreach (var attendee in evt.Attendees)
        {
            db.Notifications.Add(new Notification
            {
                UserId = attendee.UserId,
                Type = "event_invite",
                Severity = request.IsRequired ? "warning" : "info",
                Source = "events",
                Title = $"Event: {evt.Title}",
                Message = $"{creatorName} invited you to \"{evt.Title}\" on {evt.StartTime:MM/dd/yyyy hh:mm tt}.",
                EntityType = "events",
                EntityId = evt.Id,
            });
        }

        await db.SaveChangesAsync(cancellationToken);

        return await BuildResponse(evt.Id, cancellationToken);
    }

    private async Task<EventResponseModel> BuildResponse(int eventId, CancellationToken ct)
    {
        var evt = await db.Events
            .Include(e => e.Attendees)
            .Include(e => e.CalendarEventType)
            .FirstAsync(e => e.Id == eventId, ct);

        var creatorName = await db.Users
            .Where(u => u.Id == evt.CreatedByUserId)
            .Select(u => u.LastName + ", " + u.FirstName)
            .FirstOrDefaultAsync(ct) ?? "";

        var userNames = await db.Users
            .Where(u => evt.Attendees.Select(a => a.UserId).Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.LastName + ", " + u.FirstName, ct);

        return new EventResponseModel(
            evt.Id, evt.Title, evt.Description, evt.StartTime, evt.EndTime,
            evt.Location, evt.EventType.ToString(), evt.IsRequired, evt.IsCancelled,
            evt.CreatedByUserId, creatorName,
            evt.Attendees.Select(a => new EventAttendeeResponseModel(
                a.Id, a.UserId,
                userNames.GetValueOrDefault(a.UserId, ""),
                a.Status.ToString(), a.RespondedAt)).ToList(),
            evt.CreatedAt, evt.EventTypeId, evt.CalendarEventType?.SuperGroupId);
    }
}
