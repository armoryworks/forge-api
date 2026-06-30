using FluentAssertions;

using Forge.Api.Features.Events;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Events;

public class DeleteEventHandlerTests
{
    private readonly Data.Context.AppDbContext _db;
    private readonly DeleteEventHandler _handler;

    public DeleteEventHandlerTests()
    {
        _db = TestDbContextFactory.Create();
        _handler = new DeleteEventHandler(_db);
    }

    [Fact]
    public async Task Handle_SoftCancelsEvent()
    {
        var user = new ApplicationUser
        {
            UserName = "admin@test.com", Email = "admin@test.com",
            FirstName = "Admin", LastName = "User", Initials = "AU", AvatarColor = "#94a3b8",
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var evt = new Event
        {
            Title = "To Cancel",
            EventType = EventType.Meeting,
            StartTime = DateTimeOffset.UtcNow.AddDays(1),
            EndTime = DateTimeOffset.UtcNow.AddDays(1).AddHours(1),
            CreatedByUserId = user.Id,
        };
        _db.Events.Add(evt);
        await _db.SaveChangesAsync();

        await _handler.Handle(new DeleteEventCommand(evt.Id), CancellationToken.None);

        var updated = _db.Events.First(e => e.Id == evt.Id);
        updated.IsCancelled.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_NotifiesAttendees_ExcludingActor()
    {
        const int actorUserId = 10;

        var evt = new Event
        {
            Title = "Team Sync",
            EventType = EventType.Meeting,
            StartTime = DateTimeOffset.UtcNow.AddDays(1),
            EndTime = DateTimeOffset.UtcNow.AddDays(1).AddHours(1),
            CreatedByUserId = actorUserId,
            Attendees =
            {
                new EventAttendee { UserId = actorUserId, Status = AttendeeStatus.Accepted },
                new EventAttendee { UserId = 20, Status = AttendeeStatus.Invited },
                new EventAttendee { UserId = 30, Status = AttendeeStatus.Accepted },
            },
        };
        _db.Events.Add(evt);
        await _db.SaveChangesAsync();

        // Middleware sets CurrentUserId to the actor cancelling the event.
        _db.CurrentUserId = actorUserId;

        await _handler.Handle(new DeleteEventCommand(evt.Id), CancellationToken.None);

        var notifications = _db.Notifications.Where(n => n.EntityId == evt.Id).ToList();
        notifications.Should().HaveCount(2);
        notifications.Select(n => n.UserId).Should().BeEquivalentTo(new[] { 20, 30 });
        notifications.Should().OnlyContain(n =>
            n.Type == "event_cancelled" &&
            n.Source == "events" &&
            n.EntityType == "events" &&
            n.Title.Contains("Team Sync"));
    }

    [Fact]
    public async Task Handle_NonExistentEvent_ThrowsKeyNotFoundException()
    {
        var act = () => _handler.Handle(new DeleteEventCommand(999), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
