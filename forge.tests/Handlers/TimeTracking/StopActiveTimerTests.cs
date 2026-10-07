using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.TimeTracking;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.TimeTracking;

public class StopActiveTimerTests
{
    private readonly TimerTestHarness _h = new();

    [Fact]
    public async Task Handle_FiftyNineSeconds_RoundsUpToOneMinuteAndEnqueuesQuickBooks()
    {
        var user = await _h.AddUserAsync();
        var job = await _h.AddJobAsync("JOB-0042");
        var entry = await _h.AddRunningTimerAsync(user.Id, job.Id, _h.Now.AddSeconds(-59));

        var result = await _h.StopActiveTimerHandler()
            .Handle(new StopActiveTimerCommand(user.Id, _h.Now), CancellationToken.None);

        result.Should().Be(new StoppedTimerResponseModel(entry.Id, job.Id, "JOB-0042"));
        var saved = await _h.Db.TimeEntries.AsNoTracking().SingleAsync(t => t.Id == entry.Id);
        saved.DurationMinutes.Should().Be(1);
        saved.TimerStop.Should().Be(_h.Now);
        _h.SyncQueue.Verify(q => q.EnqueueAsync(
            "TimeEntry", entry.Id, "CreateTimeActivity", It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_TwentyNineSeconds_StoresZeroMinutesAndSkipsQuickBooks()
    {
        var user = await _h.AddUserAsync();
        var entry = await _h.AddRunningTimerAsync(user.Id, null, _h.Now.AddSeconds(-29));

        await _h.StopActiveTimerHandler()
            .Handle(new StopActiveTimerCommand(user.Id, _h.Now), CancellationToken.None);

        var saved = await _h.Db.TimeEntries.AsNoTracking().SingleAsync(t => t.Id == entry.Id);
        saved.DurationMinutes.Should().Be(0);
        saved.TimerStop.Should().Be(_h.Now);
        _h.SyncQueue.Verify(q => q.EnqueueAsync(
            It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NoRunningTimer_ReturnsNull()
    {
        var user = await _h.AddUserAsync();

        var result = await _h.StopActiveTimerHandler()
            .Handle(new StopActiveTimerCommand(user.Id, _h.Now), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Handle_StoppedTimer_WritesActivityRowOnTheTimeEntry()
    {
        var user = await _h.AddUserAsync();
        var entry = await _h.AddRunningTimerAsync(user.Id, null, _h.Now.AddMinutes(-45));

        await _h.StopActiveTimerHandler()
            .Handle(new StopActiveTimerCommand(user.Id, _h.Now), CancellationToken.None);

        var log = await _h.Db.ActivityLogs.AsNoTracking().SingleAsync(a => a.Action == "timer-stopped");
        log.EntityType.Should().Be("TimeEntry");
        log.EntityId.Should().Be(entry.Id);
        log.Description.Should().Be("Stopped timer at 45 min");
    }
}
