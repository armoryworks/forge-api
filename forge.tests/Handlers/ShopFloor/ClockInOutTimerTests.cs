using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.ShopFloor;
using Forge.Core.Enums;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.ShopFloor;

public class ClockInOutTimerTests
{
    private readonly TimerTestHarness _h = new();
    private readonly ClockInOutHandler _handler;

    public ClockInOutTimerTests()
    {
        _handler = new ClockInOutHandler(_h.Db, _h.ClockEventTypes.Object, _h.Mediator.Object, _h.Clock.Object);
    }

    [Fact]
    public async Task Handle_ClockOutWithRunningTimer_ClosesTimerAtPunchTime()
    {
        var user = await _h.AddUserAsync();
        var job = await _h.AddJobAsync("JOB-0100");
        var entry = await _h.AddRunningTimerAsync(user.Id, job.Id, _h.Now.AddMinutes(-90));

        var result = await _handler.Handle(new ClockInOutCommand(user.Id, "ClockOut"), CancellationToken.None);

        var clockEvent = await _h.Db.ClockEvents.AsNoTracking().SingleAsync();
        clockEvent.Timestamp.Should().Be(_h.Now);
        var saved = await _h.Db.TimeEntries.AsNoTracking().SingleAsync(t => t.Id == entry.Id);
        saved.TimerStop.Should().Be(clockEvent.Timestamp);
        saved.DurationMinutes.Should().Be(90);
        result.Should().Be(new ClockInOutResponseModel(clockEvent.Id, "JOB-0100"));
    }

    [Fact]
    public async Task Handle_ClockOutWithoutTimer_RecordsEventWithNoStoppedJob()
    {
        var user = await _h.AddUserAsync();

        var result = await _handler.Handle(new ClockInOutCommand(user.Id, "ClockOut"), CancellationToken.None);

        var clockEvent = await _h.Db.ClockEvents.AsNoTracking().SingleAsync();
        result.Should().Be(new ClockInOutResponseModel(clockEvent.Id, null));
    }

    [Theory]
    [InlineData("BreakStart")]
    [InlineData("LunchStart")]
    public async Task Handle_BreakOrLunch_LeavesTimerRunning(string code)
    {
        var user = await _h.AddUserAsync();
        var entry = await _h.AddRunningTimerAsync(user.Id, null, _h.Now.AddMinutes(-30));

        var result = await _handler.Handle(new ClockInOutCommand(user.Id, code), CancellationToken.None);

        result.StoppedJobNumber.Should().BeNull();
        var saved = await _h.Db.TimeEntries.AsNoTracking().SingleAsync(t => t.Id == entry.Id);
        saved.TimerStop.Should().BeNull();
    }

    [Fact]
    public async Task Handle_CustomOutCode_StopsTimer()
    {
        var user = await _h.AddUserAsync();
        var entry = await _h.AddRunningTimerAsync(user.Id, null, _h.Now.AddMinutes(-5));

        await _handler.Handle(new ClockInOutCommand(user.Id, "shift_end"), CancellationToken.None);

        var saved = await _h.Db.TimeEntries.AsNoTracking().SingleAsync(t => t.Id == entry.Id);
        saved.TimerStop.Should().Be(_h.Now);
    }

    [Theory]
    [InlineData("shift_end", ClockEventType.ClockOut)]
    [InlineData("smoke_break", ClockEventType.BreakStart)]
    [InlineData("return_from_errand", ClockEventType.ClockIn)]
    [InlineData("LunchStart", ClockEventType.LunchStart)]
    public async Task Handle_StoresLegacyEventTypeFromStatusMapping(string code, ClockEventType expected)
    {
        var user = await _h.AddUserAsync();

        await _handler.Handle(new ClockInOutCommand(user.Id, code), CancellationToken.None);

        var clockEvent = await _h.Db.ClockEvents.AsNoTracking().SingleAsync();
        clockEvent.EventType.Should().Be(expected);
        clockEvent.EventTypeCode.Should().Be(code);
    }

    [Fact]
    public async Task Handle_ClockOutWithRunningTimer_LogsTheEventAndTheClockOutStop()
    {
        var user = await _h.AddUserAsync();
        var job = await _h.AddJobAsync("JOB-0100");
        var entry = await _h.AddRunningTimerAsync(user.Id, job.Id, _h.Now.AddMinutes(-90));

        var result = await _handler.Handle(new ClockInOutCommand(user.Id, "ClockOut"), CancellationToken.None);

        var eventLog = await _h.Db.ActivityLogs.AsNoTracking().SingleAsync(a => a.EntityType == "ClockEvent");
        eventLog.EntityId.Should().Be(result.ClockEventId);
        eventLog.Action.Should().Be("clock-event-recorded");
        eventLog.Description.Should().Be("Clock Out via kiosk; stopped timer on JOB-0100");
        var timerLog = await _h.Db.ActivityLogs.AsNoTracking().SingleAsync(a => a.Action == "timer-stopped");
        timerLog.EntityId.Should().Be(entry.Id);
        timerLog.Description.Should().Be("Stopped timer at 90 min (clocked out)");
    }

    [Fact]
    public async Task Handle_BreakStart_LogsTheEventOnly()
    {
        var user = await _h.AddUserAsync();
        await _h.AddRunningTimerAsync(user.Id, null, _h.Now.AddMinutes(-30));

        var result = await _handler.Handle(new ClockInOutCommand(user.Id, "BreakStart"), CancellationToken.None);

        var log = await _h.Db.ActivityLogs.AsNoTracking().SingleAsync(a => a.EntityType == "ClockEvent");
        log.EntityId.Should().Be(result.ClockEventId);
        log.Description.Should().Be("Start Break via kiosk");
    }

    [Fact]
    public async Task Handle_ClockOut_StopsOperationTimersTooAndReportsTheJobTimer()
    {
        var user = await _h.AddUserAsync();
        var job = await _h.AddJobAsync("JOB-0110");
        var general = await _h.AddRunningTimerAsync(user.Id, job.Id, _h.Now.AddMinutes(-60));
        var first = await _h.AddRunningOperationTimerAsync(
            user.Id, await _h.AddJobOperationAsync(job.Id, 20), _h.Now.AddMinutes(-30));
        var second = await _h.AddRunningOperationTimerAsync(
            user.Id, await _h.AddJobOperationAsync(job.Id, 30), _h.Now.AddMinutes(-10));

        var result = await _handler.Handle(new ClockInOutCommand(user.Id, "ClockOut"), CancellationToken.None);

        result.StoppedJobNumber.Should().Be("JOB-0110");
        var ids = new[] { general.Id, first.Id, second.Id };
        var saved = await _h.Db.TimeEntries.AsNoTracking().Where(t => ids.Contains(t.Id)).ToListAsync();
        saved.Should().OnlyContain(t => t.TimerStop == _h.Now);
    }
}
