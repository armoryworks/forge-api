using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Features.TimeTracking;
using Forge.Api.Jobs;
using Forge.Core.Settings;
using Forge.Tests.Helpers;

namespace Forge.Tests.Jobs;

public class StaleTimerAlertJobTests
{
    private readonly TimerTestHarness _h = new();
    private int _staleHours = 14;

    public StaleTimerAlertJobTests()
    {
        _h.LaborSettings.Setup(s => s.GetIntAsync(ShopFloorSettings.StaleTimerHoursKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _staleHours);
    }

    private StaleTimerAlertJob Job() => new(_h.Db, _h.LaborSettings.Object, _h.Clock.Object, NullLogger<StaleTimerAlertJob>.Instance);

    private async Task<int> AddSupervisorAsync(string role)
    {
        var user = await _h.AddUserAsync();
        var row = new IdentityRole<int> { Name = role, NormalizedName = role.ToUpperInvariant() };
        _h.Db.Roles.Add(row);
        await _h.Db.SaveChangesAsync();
        _h.Db.UserRoles.Add(new IdentityUserRole<int> { UserId = user.Id, RoleId = row.Id });
        await _h.Db.SaveChangesAsync();
        return user.Id;
    }

    [Fact]
    public async Task A_timer_past_the_threshold_alerts_each_supervisor_once()
    {
        var manager = await AddSupervisorAsync("Manager");
        var productionManager = await AddSupervisorAsync("Production Manager");
        var worker = await _h.AddUserAsync();
        var job = await _h.AddJobAsync("J-STALE");
        var stale = await _h.AddRunningTimerAsync(worker.Id, job.Id, _h.Now.AddHours(-15));
        await _h.AddRunningTimerAsync(worker.Id, null, _h.Now.AddHours(-2));

        await Job().CheckStaleTimersAsync();
        await Job().CheckStaleTimersAsync();

        var sent = await _h.Db.Notifications.AsNoTracking().Where(n => n.Type == StaleTimerAlertJob.NotificationType).ToListAsync();
        sent.Select(n => n.UserId).Should().BeEquivalentTo([manager, productionManager]);
        sent.Should().OnlyContain(n => n.EntityId == stale.Id && n.EntityType == StaleTimerAlertJob.EntityType);
        sent[0].Message.Should().Contain("J-STALE").And.Contain("15 hours");
        (await _h.Db.TimeEntries.AsNoTracking().SingleAsync(t => t.Id == stale.Id)).TimerStop.Should().BeNull("timers are never stopped automatically");
    }

    [Fact]
    public async Task Timers_already_alerted_do_not_crowd_out_new_ones()
    {
        await AddSupervisorAsync("Manager");
        var worker = await _h.AddUserAsync();
        for (var i = 0; i < 501; i++)
        {
            var old = await _h.AddRunningTimerAsync(worker.Id, null, _h.Now.AddHours(-100 + i * 0.01));
            _h.Db.Notifications.Add(new Forge.Core.Entities.Notification
            {
                UserId = worker.Id,
                Type = StaleTimerAlertJob.NotificationType,
                Severity = "warning",
                Source = "time_tracking",
                Title = "Timer still running",
                Message = "earlier",
                EntityType = StaleTimerAlertJob.EntityType,
                EntityId = old.Id,
            });
        }
        await _h.Db.SaveChangesAsync();
        var fresh = await _h.AddRunningTimerAsync(worker.Id, null, _h.Now.AddHours(-20));

        await Job().CheckStaleTimersAsync();

        (await _h.Db.Notifications.AsNoTracking().CountAsync(n => n.EntityId == fresh.Id)).Should().Be(1);
    }

    [Fact]
    public async Task The_threshold_follows_the_setting()
    {
        await AddSupervisorAsync("Manager");
        var worker = await _h.AddUserAsync();
        await _h.AddRunningTimerAsync(worker.Id, null, _h.Now.AddHours(-3));

        await Job().CheckStaleTimersAsync();
        (await _h.Db.Notifications.CountAsync()).Should().Be(0);

        _staleHours = 2;
        await Job().CheckStaleTimersAsync();
        (await _h.Db.Notifications.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task The_reconciliation_report_counts_stale_timers()
    {
        _staleHours = 12;
        var worker = await _h.AddUserAsync();
        var dayStart = new DateTimeOffset(_h.Now.Year, _h.Now.Month, _h.Now.Day, 0, 0, 0, TimeSpan.Zero);
        _h.Now = dayStart.AddHours(23);
        await _h.AddRunningTimerAsync(worker.Id, null, dayStart.AddHours(1));
        await _h.AddRunningTimerAsync(worker.Id, null, dayStart.AddHours(20));
        var day = DateOnly.FromDateTime(dayStart.UtcDateTime);

        var rows = await new GetLaborReconciliationHandler(_h.Db, _h.LaborSettings.Object, _h.ClockEventTypes.Object, _h.Clock.Object)
            .Handle(new GetLaborReconciliationQuery(day, day), CancellationToken.None);

        var row = rows.Should().ContainSingle().Subject;
        row.StaleTimers.Should().Be(1);
        row.LongTimers.Should().Be(1);
        row.RunningTimers.Should().Be(2);
    }
}
