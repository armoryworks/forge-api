using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Jobs.Operations;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Jobs.Operations;

public class JobOperationTimerShopDayTests
{
    private readonly JobOperationTestHarness _h = new();

    private async Task<DateOnly> StartAtAsync(DateTimeOffset now)
    {
        _h.Timers.Now = now;
        var (job, routing) = await _h.AddJobWithRoutingAsync(10m);
        var user = await _h.Timers.AddUserAsync();

        await _h.StartHandler(user.Id).Handle(
            new StartJobOperationTimerCommand(job.Id, routing[0].Id, new StartJobOperationTimerRequestModel()),
            CancellationToken.None);

        return (await _h.Timers.Db.TimeEntries.AsNoTracking().SingleAsync(t => t.UserId == user.Id)).Date;
    }

    private async Task UseShopTimeZoneAsync(string timeZone)
    {
        _h.Timers.Db.WorkingCalendars.Add(new WorkingCalendar { Name = "Plant", TimeZone = timeZone, IsDefault = true });
        await _h.Timers.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task An_operation_timer_started_at_7_pm_mountain_is_dated_that_local_day()
    {
        await UseShopTimeZoneAsync("America/Denver");

        var date = await StartAtAsync(new DateTimeOffset(2026, 10, 8, 1, 0, 0, TimeSpan.Zero));

        date.Should().Be(new DateOnly(2026, 10, 7));
    }

    [Fact]
    public async Task An_operation_timer_started_after_local_midnight_is_dated_the_new_day()
    {
        await UseShopTimeZoneAsync("America/Denver");

        var date = await StartAtAsync(new DateTimeOffset(2026, 10, 8, 6, 30, 0, TimeSpan.Zero));

        date.Should().Be(new DateOnly(2026, 10, 8));
    }

    [Fact]
    public async Task Without_a_shop_time_zone_the_operation_timer_is_dated_the_utc_day()
    {
        var date = await StartAtAsync(new DateTimeOffset(2026, 10, 8, 1, 0, 0, TimeSpan.Zero));

        date.Should().Be(new DateOnly(2026, 10, 8));
    }
}
