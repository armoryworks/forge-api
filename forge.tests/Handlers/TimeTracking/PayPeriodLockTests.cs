using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.TimeTracking;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.TimeTracking;

public class PayPeriodLockTests
{
    private static readonly DateTimeOffset MondayMorningMountain = new(2026, 10, 19, 15, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly LastSunday = new(2026, 10, 18);
    private readonly TimerTestHarness _h = new();

    public PayPeriodLockTests()
    {
        _h.Now = MondayMorningMountain;
        _h.Db.WorkingCalendars.Add(new WorkingCalendar { Name = "Plant", TimeZone = "America/Denver", IsDefault = true });
        _h.Db.SaveChanges();
    }

    private Task<LockPayPeriodResult> LockAsync(DateTimeOffset through)
        => new LockPayPeriodHandler(_h.Db, _h.Clock.Object)
            .Handle(new LockPayPeriodCommand(through), CancellationToken.None);

    private Task<TimeEntryResponseModel> CreateAsync(int userId, DateOnly date)
        => new CreateTimeEntryHandler(_h.Repo, TestCaller.As(userId), _h.Db).Handle(
            new CreateTimeEntryCommand(new CreateTimeEntryRequestModel(null, date, 120, "Cleanup", null)),
            CancellationToken.None);

    [Fact]
    public async Task Locking_through_today_is_refused()
    {
        var act = () => LockAsync(new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*2026-10-18 or earlier*");
        (await _h.Db.SystemSettings.AnyAsync(s => s.Key == TimeLockBoundary.SettingKey)).Should().BeFalse();
    }

    [Fact]
    public async Task An_instant_is_read_as_the_shop_day_and_a_date_as_that_calendar_day()
    {
        await LockAsync(new DateTimeOffset(2026, 10, 19, 4, 0, 0, TimeSpan.Zero));
        (await TimeLockBoundary.GetAsync(_h.Db, CancellationToken.None)).Should().Be(LastSunday);

        await LockAsync(new DateTimeOffset(2026, 10, 11, 0, 0, 0, TimeSpan.Zero));
        (await TimeLockBoundary.GetAsync(_h.Db, CancellationToken.None)).Should().Be(LastSunday, "the boundary never moves back");
    }

    [Fact]
    public async Task A_new_entry_dated_inside_a_locked_period_is_refused()
    {
        var tomas = await _h.AddUserAsync();
        await CreateAsync(tomas.Id, LastSunday.AddDays(-4));

        var result = await LockAsync(new DateTimeOffset(2026, 10, 18, 23, 0, 0, TimeSpan.Zero));
        result.LockedCount.Should().Be(1);

        var act = () => CreateAsync(tomas.Id, LastSunday.AddDays(-4));
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*locked pay period*");
        (await CreateAsync(tomas.Id, LastSunday.AddDays(1))).Date.Should().Be(LastSunday.AddDays(1));
    }

    [Fact]
    public async Task Todays_entry_cannot_be_re_dated_into_a_locked_period()
    {
        var tomas = await _h.AddUserAsync();
        await LockAsync(new DateTimeOffset(2026, 10, 18, 23, 0, 0, TimeSpan.Zero));
        var entry = await CreateAsync(tomas.Id, new DateOnly(2026, 10, 19));

        var act = () => new UpdateTimeEntryHandler(_h.Repo, _h.Db, _h.Clock.Object, TestCaller.As(tomas.Id)).Handle(
            new UpdateTimeEntryCommand(entry.Id, new UpdateTimeEntryRequestModel(null, LastSunday, null, null, null)),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*locked pay period*");
        (await _h.Db.TimeEntries.AsNoTracking().SingleAsync(t => t.Id == entry.Id)).Date.Should().Be(new DateOnly(2026, 10, 19));
    }

    [Fact]
    public async Task Rows_already_flagged_locked_do_not_invent_a_boundary()
    {
        var ana = await _h.AddUserAsync();
        _h.Db.TimeEntries.Add(new TimeEntry { UserId = ana.Id, Date = new DateOnly(2026, 9, 30), DurationMinutes = 60, IsLocked = true });
        await _h.Db.SaveChangesAsync();

        (await CreateAsync(ana.Id, new DateOnly(2026, 9, 29))).Date.Should().Be(new DateOnly(2026, 9, 29));
    }
}
