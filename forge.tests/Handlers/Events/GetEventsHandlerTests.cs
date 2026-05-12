using FluentAssertions;

using Forge.Api.Features.Events;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Events;

public class GetEventsHandlerTests
{
    private readonly Data.Context.AppDbContext _db;
    private readonly GetEventsHandler _handler;

    public GetEventsHandlerTests()
    {
        _db = TestDbContextFactory.Create();
        _handler = new GetEventsHandler(_db);
    }

    private async Task<ApplicationUser> SeedUser()
    {
        var user = new ApplicationUser
        {
            UserName = "admin@test.com",
            Email = "admin@test.com",
            FirstName = "Admin",
            LastName = "User",
            Initials = "AU",
            AvatarColor = "#94a3b8",
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    private async Task<Event> SeedEvent(int creatorId, string title, EventType type,
        DateTimeOffset start, bool isCancelled = false)
    {
        var evt = new Event
        {
            Title = title,
            EventType = type,
            StartTime = start,
            EndTime = start.AddHours(1),
            CreatedByUserId = creatorId,
            IsCancelled = isCancelled,
        };
        _db.Events.Add(evt);
        await _db.SaveChangesAsync();
        return evt;
    }

    [Fact]
    public async Task Handle_ReturnsAllNonCancelledEvents()
    {
        var user = await SeedUser();
        await SeedEvent(user.Id, "Active Meeting", EventType.Meeting, DateTimeOffset.UtcNow.AddDays(1));
        await SeedEvent(user.Id, "Cancelled Meeting", EventType.Meeting, DateTimeOffset.UtcNow.AddDays(2), isCancelled: true);
        await SeedEvent(user.Id, "Training", EventType.Training, DateTimeOffset.UtcNow.AddDays(3));

        var result = await _handler.Handle(new GetEventsQuery(null, null, null), CancellationToken.None);

        result.Should().HaveCount(2);
        result.Select(e => e.Title).Should().Contain("Active Meeting").And.Contain("Training");
    }

    [Fact]
    public async Task Handle_FiltersbyEventType()
    {
        var user = await SeedUser();
        await SeedEvent(user.Id, "Meeting 1", EventType.Meeting, DateTimeOffset.UtcNow.AddDays(1));
        await SeedEvent(user.Id, "Training 1", EventType.Training, DateTimeOffset.UtcNow.AddDays(2));

        var result = await _handler.Handle(new GetEventsQuery(null, null, "Training"), CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].Title.Should().Be("Training 1");
    }

    [Fact]
    public async Task Handle_FiltersByDateRange()
    {
        var user = await SeedUser();
        var now = DateTimeOffset.UtcNow;
        await SeedEvent(user.Id, "Past", EventType.Meeting, now.AddDays(-5));
        await SeedEvent(user.Id, "Current", EventType.Meeting, now.AddDays(1));
        await SeedEvent(user.Id, "Future", EventType.Meeting, now.AddDays(30));

        var result = await _handler.Handle(
            new GetEventsQuery(now.AddDays(-1), now.AddDays(5), null), CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].Title.Should().Be("Current");
    }

    [Fact]
    public async Task Handle_OrdersByStartTime()
    {
        var user = await SeedUser();
        var now = DateTimeOffset.UtcNow;
        await SeedEvent(user.Id, "Later", EventType.Meeting, now.AddDays(5));
        await SeedEvent(user.Id, "Earlier", EventType.Meeting, now.AddDays(1));
        await SeedEvent(user.Id, "Middle", EventType.Meeting, now.AddDays(3));

        var result = await _handler.Handle(new GetEventsQuery(null, null, null), CancellationToken.None);

        result.Select(e => e.Title).Should().ContainInOrder("Earlier", "Middle", "Later");
    }
}
