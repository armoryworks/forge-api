using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.TimeTracking;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.TimeTracking;

public class TimeEntryShopDayTests
{
    private static readonly DateTimeOffset SevenPmMountain = new(2026, 10, 8, 1, 0, 0, TimeSpan.Zero);
    private readonly TimerTestHarness _h = new();

    private async Task UseShopTimeZoneAsync(string timeZone)
    {
        _h.Db.WorkingCalendars.Add(new WorkingCalendar { Name = "Plant", TimeZone = timeZone, IsDefault = true });
        await _h.Db.SaveChangesAsync();
    }

    private async Task<(ApplicationUser User, TimeEntry Entry)> StartTimerAsync(DateTimeOffset at)
    {
        _h.Now = at;
        var user = await _h.AddUserAsync();
        await new StartTimerHandler(
                _h.Repo, _h.Jobs, _h.Db, _h.SignedIn(user.Id), _h.TimerHub.Object, _h.Mediator.Object, _h.Clock.Object)
            .Handle(new StartTimerCommand(new StartTimerRequestModel(null, null, null)), CancellationToken.None);
        return (user, await _h.Db.TimeEntries.SingleAsync(t => t.UserId == user.Id));
    }

    private Task UpdateNotesAsync(int entryId) =>
        new UpdateTimeEntryHandler(_h.Repo, _h.Db, _h.Clock.Object).Handle(
            new UpdateTimeEntryCommand(entryId, new UpdateTimeEntryRequestModel(null, null, null, null, "Second shift")),
            CancellationToken.None);

    [Fact]
    public async Task A_timer_started_at_7_pm_mountain_can_be_edited_the_same_evening()
    {
        await UseShopTimeZoneAsync("America/Denver");
        var (_, entry) = await StartTimerAsync(SevenPmMountain);
        _h.Now = SevenPmMountain.AddMinutes(30);

        await UpdateNotesAsync(entry.Id);

        (await _h.Db.TimeEntries.AsNoTracking().SingleAsync(t => t.Id == entry.Id)).Notes.Should().Be("Second shift");
    }

    [Fact]
    public async Task A_timer_started_at_7_pm_mountain_can_be_deleted_the_same_evening()
    {
        await UseShopTimeZoneAsync("America/Denver");
        var (user, entry) = await StartTimerAsync(SevenPmMountain);
        _h.Now = SevenPmMountain.AddMinutes(30);

        await new DeleteTimeEntryHandler(_h.Repo, _h.SignedIn(user.Id), _h.Db, _h.Clock.Object)
            .Handle(new DeleteTimeEntryCommand(entry.Id), CancellationToken.None);

        (await _h.Db.TimeEntries.IgnoreQueryFilters().AsNoTracking().SingleAsync(t => t.Id == entry.Id))
            .DeletedAt.Should().Be(_h.Now);
    }

    [Fact]
    public async Task An_entry_from_the_previous_shop_day_still_cannot_be_edited()
    {
        await UseShopTimeZoneAsync("America/Denver");
        var (_, entry) = await StartTimerAsync(SevenPmMountain);
        _h.Now = SevenPmMountain.AddHours(6);

        var act = () => UpdateNotesAsync(entry.Id);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Time entries from previous days cannot be edited.");
    }

    [Fact]
    public async Task A_timer_resumed_the_same_evening_is_dated_the_shop_day()
    {
        await UseShopTimeZoneAsync("America/Denver");
        var user = await _h.AddUserAsync();
        var stoppedAt = SevenPmMountain.AddMinutes(30);
        var stopped = new TimeEntry
        {
            UserId = user.Id,
            Date = new DateOnly(2026, 10, 7),
            TimerStart = SevenPmMountain,
            TimerStop = stoppedAt,
            DurationMinutes = 30,
            IsManual = false,
        };
        _h.Db.TimeEntries.Add(stopped);
        await _h.Db.SaveChangesAsync();
        _h.Db.SyncQueueEntries.Add(new SyncQueueEntry
        {
            EntityType = "TimeEntry",
            EntityId = stopped.Id,
            Operation = "CreateTimeActivity",
            Status = SyncStatus.Completed,
        });
        await _h.Db.SaveChangesAsync();

        var resumed = await new ResumeStoppedTimerHandler(_h.Repo, _h.Db, _h.TimerHub.Object)
            .Handle(new ResumeStoppedTimerCommand(user.Id, stoppedAt), CancellationToken.None);

        resumed.Should().NotBeNull();
        resumed!.Id.Should().NotBe(stopped.Id);
        (await _h.Db.TimeEntries.AsNoTracking().SingleAsync(t => t.Id == resumed.Id)).Date
            .Should().Be(new DateOnly(2026, 10, 7));
    }
}
