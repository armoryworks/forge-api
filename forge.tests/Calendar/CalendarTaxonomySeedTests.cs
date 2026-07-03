using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Data;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Tests.Helpers;

namespace Forge.Tests.Calendar;

/// <summary>
/// compliance-calendar A-1, Stage 1b. Verifies the calendar-taxonomy seeder creates the
/// four legacy-promoted event types, is idempotent, and backfills an existing legacy
/// event's <c>EventTypeId</c> from its enum value.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CalendarTaxonomySeedTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Seed_creates_types_and_backfills_legacy_events()
    {
        await using var db = fixture.CreateContext();
        await db.Events.ExecuteDeleteAsync();
        await db.CalendarEventTypes.ExecuteDeleteAsync();
        await db.CalendarSuperGroups.ExecuteDeleteAsync();

        var ev = new Event
        {
            Title = "Weekly standup",
            StartTime = DateTimeOffset.UtcNow,
            EndTime = DateTimeOffset.UtcNow.AddHours(1),
            EventType = EventType.Meeting,
            CreatedByUserId = 1,
        };
        db.Events.Add(ev);
        await db.SaveChangesAsync();

        await SeedData.SeedCalendarTaxonomyAsync(db);
        await SeedData.SeedCalendarTaxonomyAsync(db); // idempotent — no duplicates

        await using var verify = fixture.CreateContext();
        (await verify.CalendarEventTypes.CountAsync()).Should().Be(4);
        (await verify.CalendarSuperGroups.CountAsync()).Should().Be(3);

        var meeting = await verify.CalendarEventTypes.SingleAsync(t => t.Key == "meeting");
        var reloaded = await verify.Events.SingleAsync(e => e.Id == ev.Id);
        reloaded.EventTypeId.Should().Be(meeting.Id, "the legacy Meeting event backfills to the 'meeting' type");
    }
}
