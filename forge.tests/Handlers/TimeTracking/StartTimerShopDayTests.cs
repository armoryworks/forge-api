using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.TimeTracking;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.TimeTracking;

public class StartTimerShopDayTests
{
    private readonly TimerTestHarness _h = new();

    private async Task<DateOnly> StartAtAsync(DateTimeOffset now)
    {
        _h.Now = now;
        var user = await _h.AddUserAsync();
        var handler = new StartTimerHandler(
            _h.Repo, _h.Jobs, _h.Db, _h.SignedIn(user.Id), _h.TimerHub.Object, _h.Mediator.Object, _h.Clock.Object);

        await handler.Handle(new StartTimerCommand(new StartTimerRequestModel(null, null, null)), CancellationToken.None);

        return (await _h.Db.TimeEntries.AsNoTracking().SingleAsync(t => t.UserId == user.Id)).Date;
    }

    private async Task UseShopTimeZoneAsync(string timeZone)
    {
        _h.Db.WorkingCalendars.Add(new WorkingCalendar { Name = "Plant", TimeZone = timeZone, IsDefault = true });
        await _h.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task A_timer_started_at_7_pm_mountain_is_dated_that_local_day()
    {
        await UseShopTimeZoneAsync("America/Denver");

        var date = await StartAtAsync(new DateTimeOffset(2026, 10, 8, 1, 0, 0, TimeSpan.Zero));

        date.Should().Be(new DateOnly(2026, 10, 7));
    }

    [Fact]
    public async Task A_timer_started_after_local_midnight_is_dated_the_new_day()
    {
        await UseShopTimeZoneAsync("America/Denver");

        var date = await StartAtAsync(new DateTimeOffset(2026, 10, 8, 6, 30, 0, TimeSpan.Zero));

        date.Should().Be(new DateOnly(2026, 10, 8));
    }

    [Fact]
    public async Task Without_a_shop_time_zone_the_timer_is_dated_the_utc_day()
    {
        var date = await StartAtAsync(new DateTimeOffset(2026, 10, 8, 1, 0, 0, TimeSpan.Zero));

        date.Should().Be(new DateOnly(2026, 10, 8));
    }
}
