using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Events;
using Forge.Core.Entities;
using Forge.Core.Entities.Calendar;
using Forge.Core.Enums;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Calendar;

/// <summary>
/// compliance-calendar A-4, Stage 5. Workflow status updates apply only to tracking-tier
/// events (type/group RequiresTracking) and stamp completion on Done.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class EventWorkflowHandlerTests(PostgresFixture fixture)
{
    private static async Task ResetAsync(Forge.Data.Context.AppDbContext db)
    {
        await db.Events.ExecuteDeleteAsync();
        await db.CalendarEventTypes.ExecuteDeleteAsync();
        await db.CalendarSuperGroups.ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Status_update_on_tracking_event_stamps_completion()
    {
        await using var db = fixture.CreateContext();
        await ResetAsync(db);

        var group = new CalendarSuperGroup { Key = "comp", Name = "Comp", RequiresTracking = true, SortOrder = 1 };
        db.CalendarSuperGroups.Add(group);
        await db.SaveChangesAsync();
        var type = new CalendarEventType { SuperGroupId = group.Id, Key = "osha", Name = "OSHA", RequiresTracking = true, SortOrder = 1 };
        db.CalendarEventTypes.Add(type);
        await db.SaveChangesAsync();
        var evt = new Event { Title = "File 300A", StartTime = DateTimeOffset.UtcNow, EndTime = DateTimeOffset.UtcNow.AddHours(1), EventType = EventType.Other, EventTypeId = type.Id, CreatedByUserId = 1 };
        db.Events.Add(evt);
        await db.SaveChangesAsync();

        db.CurrentUserId = 1;
        await new UpdateEventStatusHandler(db, new SystemClock())
            .Handle(new UpdateEventStatusCommand(evt.Id, "Done", 1, null, "https://osha.gov/filed", null), default);

        await using var verify = fixture.CreateContext();
        var reloaded = await verify.Events.SingleAsync(e => e.Id == evt.Id);
        reloaded.Status.Should().Be(EventStatus.Done);
        reloaded.CompletedAt.Should().NotBeNull();
        reloaded.OwnerUserId.Should().Be(1);
        reloaded.EvidenceUrl.Should().Be("https://osha.gov/filed");
    }

    [Fact]
    public async Task Status_update_rejected_for_reminder_tier_event()
    {
        await using var db = fixture.CreateContext();
        await ResetAsync(db);

        var group = new CalendarSuperGroup { Key = "ops", Name = "Ops", RequiresTracking = false, SortOrder = 1 };
        db.CalendarSuperGroups.Add(group);
        await db.SaveChangesAsync();
        var type = new CalendarEventType { SuperGroupId = group.Id, Key = "mtg", Name = "Meeting", RequiresTracking = false, SortOrder = 1 };
        db.CalendarEventTypes.Add(type);
        await db.SaveChangesAsync();
        var evt = new Event { Title = "Standup", StartTime = DateTimeOffset.UtcNow, EndTime = DateTimeOffset.UtcNow.AddHours(1), EventType = EventType.Meeting, EventTypeId = type.Id, CreatedByUserId = 1 };
        db.Events.Add(evt);
        await db.SaveChangesAsync();

        var act = async () => await new UpdateEventStatusHandler(db, new SystemClock())
            .Handle(new UpdateEventStatusCommand(evt.Id, "Done", null, null, null, null), default);
        await act.Should().ThrowAsync<InvalidOperationException>("reminder-tier events have no workflow status");
    }
}
