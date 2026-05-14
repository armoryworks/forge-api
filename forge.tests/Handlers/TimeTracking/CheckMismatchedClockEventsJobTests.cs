using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

using Forge.Api.Jobs;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.TimeTracking;

public class CheckMismatchedClockEventsJobTests
{
    private readonly Data.Context.AppDbContext _db;
    private readonly CheckMismatchedClockEventsJob _job;

    public CheckMismatchedClockEventsJobTests()
    {
        _db = TestDbContextFactory.Create();
        var logger = new Mock<ILogger<CheckMismatchedClockEventsJob>>();

        var clockEventTypeService = new Mock<IClockEventTypeService>();
        clockEventTypeService.Setup(s => s.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClockEventTypeDefinition>
            {
                new("ClockIn", "Clock In", "In", "ClockOut", "work", true, true, "login", "#22c55e"),
                new("ClockOut", "Clock Out", "Out", "ClockIn", "work", false, false, "logout", "#ef4444"),
                new("BreakStart", "Start Break", "OnBreak", "BreakEnd", "break", true, true, "free_breakfast", "#f59e0b"),
                new("BreakEnd", "End Break", "In", "BreakStart", "break", true, false, "play_arrow", "#22c55e"),
                new("LunchStart", "Start Lunch", "OnLunch", "LunchEnd", "lunch", true, true, "restaurant", "#f59e0b"),
                new("LunchEnd", "End Lunch", "In", "LunchStart", "lunch", true, false, "play_arrow", "#22c55e"),
            });

        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(() => DateTimeOffset.UtcNow);

        _job = new CheckMismatchedClockEventsJob(_db, clockEventTypeService.Object, clock.Object, logger.Object);
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
    public async Task CheckMismatchedEventsAsync_NoEvents_DoesNotCreateNotifications()
    {
        await _job.CheckMismatchedEventsAsync();

        _db.Notifications.Should().BeEmpty();
    }

    [Fact]
    public async Task CheckMismatchedEventsAsync_MatchedClockInOut_DoesNotCreateNotifications()
    {
        var user = await SeedUser("Worker", "One");
        var yesterday = DateTime.UtcNow.AddDays(-1);

        _db.ClockEvents.AddRange(
            new ClockEvent { UserId = user.Id, EventType = ClockEventType.ClockIn, EventTypeCode = "ClockIn", Timestamp = new DateTimeOffset(yesterday.Date.AddHours(8), TimeSpan.Zero) },
            new ClockEvent { UserId = user.Id, EventType = ClockEventType.ClockOut, EventTypeCode = "ClockOut", Timestamp = new DateTimeOffset(yesterday.Date.AddHours(17), TimeSpan.Zero) });
        await _db.SaveChangesAsync();

        await _job.CheckMismatchedEventsAsync();

        var notifications = _db.Notifications.Where(n => n.Type == "mismatched_clock_event").ToList();
        notifications.Should().BeEmpty();
    }

    [Fact]
    public async Task CheckMismatchedEventsAsync_OrphanedClockIn_CreatesNotificationForEmployee()
    {
        var worker = await SeedUser("Forgot", "Clockout");
        var yesterday = DateTime.UtcNow.AddDays(-1);

        _db.ClockEvents.Add(new ClockEvent
        {
            UserId = worker.Id,
            EventType = ClockEventType.ClockIn,
            EventTypeCode = "ClockIn",
            Timestamp = new DateTimeOffset(yesterday.Date.AddHours(8), TimeSpan.Zero),
        });
        await _db.SaveChangesAsync();

        await _job.CheckMismatchedEventsAsync();

        var notifications = _db.Notifications
            .Where(n => n.UserId == worker.Id && n.Type == "mismatched_clock_event")
            .ToList();

        notifications.Should().HaveCount(1);
        notifications[0].Severity.Should().Be("warning");
        notifications[0].Title.Should().Contain("Unmatched");
    }

    [Fact]
    public async Task CheckMismatchedEventsAsync_AlreadyNotified_DoesNotDuplicate()
    {
        var worker = await SeedUser("Already", "Notified");
        var yesterday = DateTime.UtcNow.AddDays(-1);
        var dayStart = DateOnly.FromDateTime(yesterday).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        _db.ClockEvents.Add(new ClockEvent
        {
            UserId = worker.Id,
            EventType = ClockEventType.ClockIn,
            EventTypeCode = "ClockIn",
            Timestamp = new DateTimeOffset(yesterday.Date.AddHours(8), TimeSpan.Zero),
        });

        // Pre-existing notification
        _db.Notifications.Add(new Notification
        {
            UserId = worker.Id,
            Type = "mismatched_clock_event",
            Severity = "warning",
            Source = "time_tracking",
            Title = "Unmatched Clock-In",
            Message = "Already notified",
        });
        await _db.SaveChangesAsync();

        await _job.CheckMismatchedEventsAsync();

        var notifications = _db.Notifications
            .Where(n => n.UserId == worker.Id && n.Type == "mismatched_clock_event")
            .ToList();

        notifications.Should().HaveCount(1); // Still just the original one
    }
}
