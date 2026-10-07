using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.TimeTracking;
using Forge.Core.Models;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.TimeTracking;

public class CreateClockEventTimerTests
{
    private readonly TimerTestHarness _h = new();

    private CreateClockEventHandler Handler(int userId) => new(
        _h.Db, _h.SignedIn(userId), _h.ClockEventTypes.Object, _h.Mediator.Object, _h.Clock.Object);

    [Fact]
    public async Task Handle_ClockOutFromDesktop_StopsTheRunningTimerAndLogsTheEvent()
    {
        var user = await _h.AddUserAsync();
        var job = await _h.AddJobAsync("JOB-0300");
        var entry = await _h.AddRunningTimerAsync(user.Id, job.Id, _h.Now.AddMinutes(-60));

        var result = await Handler(user.Id).Handle(
            new CreateClockEventCommand(new CreateClockEventRequestModel("ClockOut", null, null, "desktop")),
            CancellationToken.None);

        var saved = await _h.Db.TimeEntries.AsNoTracking().SingleAsync(t => t.Id == entry.Id);
        saved.TimerStop.Should().Be(result.Timestamp);
        saved.DurationMinutes.Should().Be(60);
        var log = await _h.Db.ActivityLogs.AsNoTracking().SingleAsync(a => a.EntityType == "ClockEvent");
        log.EntityId.Should().Be(result.Id);
        log.Description.Should().Be("Clock Out via desktop; stopped timer on JOB-0300");
    }

    [Fact]
    public async Task Handle_LunchFromDesktop_LeavesTheTimerRunning()
    {
        var user = await _h.AddUserAsync();
        var entry = await _h.AddRunningTimerAsync(user.Id, null, _h.Now.AddMinutes(-60));

        await Handler(user.Id).Handle(
            new CreateClockEventCommand(new CreateClockEventRequestModel("LunchStart", null, null, null)),
            CancellationToken.None);

        var saved = await _h.Db.TimeEntries.AsNoTracking().SingleAsync(t => t.Id == entry.Id);
        saved.TimerStop.Should().BeNull();
        var log = await _h.Db.ActivityLogs.AsNoTracking().SingleAsync(a => a.EntityType == "ClockEvent");
        log.Description.Should().Be("Start Lunch");
    }
}
