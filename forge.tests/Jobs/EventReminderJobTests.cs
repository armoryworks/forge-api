using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

using Forge.Api.Jobs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Jobs;

public class EventReminderJobTests
{
    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private static async Task<Event> SeedEventAsync(AppDbContext db, DateTimeOffset start, bool isAllDay)
    {
        db.WorkingCalendars.Add(new WorkingCalendar { Name = "Plant", TimeZone = "America/Denver", IsDefault = true });
        var evt = new Event
        {
            Title = "Ship SO-1001",
            EventType = EventType.Other,
            StartTime = start,
            EndTime = start,
            IsAllDay = isAllDay,
            CreatedByUserId = 10,
            Attendees = { new EventAttendee { UserId = 20, Status = AttendeeStatus.Invited } },
        };
        db.Events.Add(evt);
        await db.SaveChangesAsync();
        return evt;
    }

    [Fact]
    public async Task TimedEvent_ReminderShowsTheStartInShopTime()
    {
        using var db = TestDbContextFactory.Create();
        var start = new DateTimeOffset(2026, 10, 20, 15, 30, 0, TimeSpan.Zero);
        var evt = await SeedEventAsync(db, start, isAllDay: false);
        var job = new EventReminderJob(db, new FixedClock(start.AddMinutes(-20)), NullLogger<EventReminderJob>.Instance);

        await job.SendRemindersAsync();

        db.Notifications.Single(n => n.EntityId == evt.Id).Message
            .Should().Be("\"Ship SO-1001\" starts at 09:30 AM.");
    }

    [Fact]
    public async Task AllDayEvent_ReminderShowsTheDateNotAClockTime()
    {
        using var db = TestDbContextFactory.Create();
        var day = new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero);
        var evt = await SeedEventAsync(db, day, isAllDay: true);
        var job = new EventReminderJob(db, new FixedClock(day.AddMinutes(-20)), NullLogger<EventReminderJob>.Instance);

        await job.SendRemindersAsync();

        db.Notifications.Single(n => n.EntityId == evt.Id).Message
            .Should().Be("\"Ship SO-1001\" is on 10/20/2026.");
    }
}
