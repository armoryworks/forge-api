using FluentAssertions;

using Forge.Api.Features.Events;
using Forge.Core.Entities;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Events;

public class GetUpcomingEventsForUserHandlerTests
{
    private readonly Data.Context.AppDbContext _db;
    private readonly GetUpcomingEventsForUserHandler _handler;

    public GetUpcomingEventsForUserHandlerTests()
    {
        _db = TestDbContextFactory.Create();
        _handler = new GetUpcomingEventsForUserHandler(_db);
    }

    private async Task<ApplicationUser> SeedUser(string first, string last)
    {
        var user = new ApplicationUser
        {
            UserName = $"{first.ToLower()}@test.com",
            Email = $"{first.ToLower()}@test.com",
            FirstName = first, LastName = last,
            Initials = $"{first[0]}{last[0]}", AvatarColor = "#94a3b8",
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task Handle_ReturnsOnlyUserUpcomingEvents()
    {
        var user1 = await SeedUser("John", "Doe");
        var user2 = await SeedUser("Jane", "Smith");
        var creator = await SeedUser("Admin", "User");

        // Event with user1 as attendee
        var evt1 = new Event
        {
            Title = "User1 Meeting",
            EventType = EventType.Meeting,
            StartTime = DateTimeOffset.UtcNow.AddDays(1),
            EndTime = DateTimeOffset.UtcNow.AddDays(1).AddHours(1),
            CreatedByUserId = creator.Id,
        };
        evt1.Attendees.Add(new EventAttendee { UserId = user1.Id, Status = AttendeeStatus.Invited });
        _db.Events.Add(evt1);

        // Event with user2 only
        var evt2 = new Event
        {
            Title = "User2 Training",
            EventType = EventType.Training,
            StartTime = DateTimeOffset.UtcNow.AddDays(2),
            EndTime = DateTimeOffset.UtcNow.AddDays(2).AddHours(1),
            CreatedByUserId = creator.Id,
        };
        evt2.Attendees.Add(new EventAttendee { UserId = user2.Id, Status = AttendeeStatus.Invited });
        _db.Events.Add(evt2);

        await _db.SaveChangesAsync();

        var result = await _handler.Handle(new GetUpcomingEventsForUserQuery(user1.Id), CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].Title.Should().Be("User1 Meeting");
    }

    [Fact]
    public async Task Handle_ExcludesCancelledEvents()
    {
        var user = await SeedUser("Worker", "One");
        var creator = await SeedUser("Admin", "User");

        var evt = new Event
        {
            Title = "Cancelled Event",
            EventType = EventType.Safety,
            StartTime = DateTimeOffset.UtcNow.AddDays(1),
            EndTime = DateTimeOffset.UtcNow.AddDays(1).AddHours(1),
            CreatedByUserId = creator.Id,
            IsCancelled = true,
        };
        evt.Attendees.Add(new EventAttendee { UserId = user.Id, Status = AttendeeStatus.Invited });
        _db.Events.Add(evt);
        await _db.SaveChangesAsync();

        var result = await _handler.Handle(new GetUpcomingEventsForUserQuery(user.Id), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ExcludesPastEvents()
    {
        var user = await SeedUser("Worker", "Two");
        var creator = await SeedUser("Admin", "User");

        var evt = new Event
        {
            Title = "Past Event",
            EventType = EventType.Meeting,
            StartTime = DateTimeOffset.UtcNow.AddDays(-2),
            EndTime = DateTimeOffset.UtcNow.AddDays(-2).AddHours(1),
            CreatedByUserId = creator.Id,
        };
        evt.Attendees.Add(new EventAttendee { UserId = user.Id, Status = AttendeeStatus.Invited });
        _db.Events.Add(evt);
        await _db.SaveChangesAsync();

        var result = await _handler.Handle(new GetUpcomingEventsForUserQuery(user.Id), CancellationToken.None);

        result.Should().BeEmpty();
    }
}
