using FluentAssertions;

using Forge.Api.Features.TimeTracking;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.TimeTracking;

public class LaborLockAndHoursTests
{
    private readonly TimerTestHarness _h = new();

    [Fact]
    public async Task A_pay_period_cannot_be_locked_while_a_timer_started_in_it_is_still_running()
    {
        var user = await _h.AddUserAsync();
        await _h.AddRunningTimerAsync(user.Id, null, _h.Now.AddDays(-2));

        var act = () => new LockPayPeriodHandler(_h.Db, _h.Clock.Object)
            .Handle(new LockPayPeriodCommand(_h.Now.AddDays(-1).Date), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*still running*Stop it*");
        (await TimeLockBoundary.GetAsync(_h.Db, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task A_timer_started_after_the_lock_date_does_not_block_the_lock()
    {
        var user = await _h.AddUserAsync();
        await _h.AddRunningTimerAsync(user.Id, null, _h.Now.AddMinutes(-30));

        var result = await new LockPayPeriodHandler(_h.Db, _h.Clock.Object)
            .Handle(new LockPayPeriodCommand(_h.Now.AddDays(-1).Date), CancellationToken.None);

        result.LockedCount.Should().Be(0);
    }

    [Fact]
    public async Task Worked_hours_count_overlapping_timers_once()
    {
        var user = await _h.AddUserAsync();
        foreach (var offset in new[] { 0, 0, 30 })
        {
            _h.Db.TimeEntries.Add(new TimeEntry
            {
                UserId = user.Id,
                Date = DateOnly.FromDateTime(_h.Now.UtcDateTime),
                TimerStart = _h.Now.AddMinutes(-120 + offset),
                TimerStop = _h.Now.AddMinutes(-60 + offset),
                DurationMinutes = 60,
            });
        }
        _h.Db.TimeEntries.Add(new TimeEntry
        {
            UserId = user.Id, Date = DateOnly.FromDateTime(_h.Now.UtcDateTime), DurationMinutes = 15, IsManual = true,
        });
        await _h.Db.SaveChangesAsync();

        var hours = await new WorkedHoursService(_h.Db, _h.ClockEventTypes.Object, _h.Clock.Object)
            .DailyHoursAsync([user.Id], DateOnly.FromDateTime(_h.Now.UtcDateTime), DateOnly.FromDateTime(_h.Now.UtcDateTime), CancellationToken.None);

        hours[user.Id][DateOnly.FromDateTime(_h.Now.UtcDateTime)].Should().Be(105m / 60m);
    }
    [Fact]
    public async Task Labor_reconciliation_compares_paid_time_with_the_labor_charged_to_jobs()
    {
        var user = await _h.AddUserAsync();
        var job = await _h.AddJobAsync("J-RECON");
        var dayStart = new DateTimeOffset(_h.Now.UtcDateTime.Date, TimeSpan.Zero);
        await _h.AddClockEventAsync(user.Id, "ClockIn", dayStart.AddHours(5));
        await _h.AddClockEventAsync(user.Id, "LunchStart", dayStart.AddHours(9));
        await _h.AddClockEventAsync(user.Id, "LunchEnd", dayStart.AddHours(9).AddMinutes(30));
        var first = await _h.AddRunningTimerAsync(user.Id, job.Id, dayStart.AddHours(6));
        var second = await _h.AddRunningTimerAsync(user.Id, job.Id, dayStart.AddHours(6));
        var indirect = await _h.AddRunningTimerAsync(user.Id, null, dayStart.AddHours(12));
        await _h.AddRunningTimerAsync(user.Id, job.Id, dayStart.AddHours(13).AddMinutes(30));
        _h.Now = dayStart.AddHours(12);
        await _h.StopActiveTimerHandler().Handle(new StopActiveTimerCommand(user.Id, _h.Now, TimeEntryId: first.Id), CancellationToken.None);
        await _h.StopActiveTimerHandler().Handle(new StopActiveTimerCommand(user.Id, _h.Now, TimeEntryId: second.Id), CancellationToken.None);
        _h.Now = dayStart.AddHours(13);
        await _h.StopActiveTimerHandler().Handle(new StopActiveTimerCommand(user.Id, _h.Now, TimeEntryId: indirect.Id), CancellationToken.None);
        _h.Now = dayStart.AddHours(14);
        var day = DateOnly.FromDateTime(dayStart.UtcDateTime);

        var rows = await new GetLaborReconciliationHandler(_h.Db, _h.LaborSettings.Object, _h.ClockEventTypes.Object, _h.Clock.Object)
            .Handle(new GetLaborReconciliationQuery(day, day), CancellationToken.None);

        var row = rows.Should().ContainSingle().Subject;
        row.PaidMinutes.Should().Be(510m, "05:00 to 14:00 is nine hours less a 30-minute lunch");
        row.JobLaborMinutes.Should().Be(330m, "two timers over 06:00-12:00 share 5.5 paid hours");
        row.IndirectMinutes.Should().Be(60m);
        row.UnallocatedMinutes.Should().Be(120m);
        row.OverlappingTimerMinutes.Should().Be(360m);
        row.RunningTimers.Should().Be(1);
        row.LongTimers.Should().Be(0);
    }
}
