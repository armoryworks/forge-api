using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.Mobile;
using Forge.Api.Features.ShopFloor;
using Forge.Api.Services;
using Forge.Core.Enums;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Mobile;

public class UndoClockPunchTimerTests
{
    private readonly TimerTestHarness _h = new();
    private readonly ClockInOutHandler _punch;

    public UndoClockPunchTimerTests()
    {
        _punch = new ClockInOutHandler(_h.Db, _h.ClockEventTypes.Object, _h.Mediator.Object, _h.Clock.Object);
        _h.Mediator
            .Setup(m => m.Send(It.IsAny<GetClockStateQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ClockStateResponseModel)null!);
    }

    private UndoClockPunchHandler UndoHandler(int userId) => new(
        _h.Db, _h.Mediator.Object, _h.Clock.Object, _h.SignedIn(userId),
        _h.ClockEventTypes.Object, Mock.Of<ISystemAuditWriter>());

    [Fact]
    public async Task Handle_UndoneClockOut_ResumesTheTimerAndDropsTheQuickBooksItem()
    {
        var user = await _h.AddUserAsync();
        var job = await _h.AddJobAsync("JOB-0200");
        var entry = await _h.AddRunningTimerAsync(user.Id, job.Id, _h.Now.AddMinutes(-90));
        var punch = await _punch.Handle(new ClockInOutCommand(user.Id, "ClockOut"), CancellationToken.None);
        (await _h.Db.SyncQueueEntries.CountAsync()).Should().Be(1);

        _h.Now = _h.Now.AddSeconds(20);
        await UndoHandler(user.Id).Handle(new UndoClockPunchCommand(punch.ClockEventId), CancellationToken.None);

        (await _h.Db.ClockEvents.AnyAsync()).Should().BeFalse();
        var saved = await _h.Db.TimeEntries.AsNoTracking().SingleAsync();
        saved.Id.Should().Be(entry.Id);
        saved.TimerStop.Should().BeNull();
        saved.DurationMinutes.Should().Be(0);
        (await _h.Db.SyncQueueEntries.AnyAsync()).Should().BeFalse();
        var log = await _h.Db.ActivityLogs.AsNoTracking().SingleAsync(a => a.Action == "timer-resumed");
        log.EntityId.Should().Be(entry.Id);
        _h.UserGroup.Verify(g => g.SendCoreAsync(
            "timerStarted", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_UndoneClockOutAlreadySentToQuickBooks_ContinuesOnANewEntry()
    {
        var user = await _h.AddUserAsync();
        var job = await _h.AddJobAsync("JOB-0201");
        var entry = await _h.AddRunningTimerAsync(user.Id, job.Id, _h.Now.AddMinutes(-90));
        var punchedAt = _h.Now;
        var punch = await _punch.Handle(new ClockInOutCommand(user.Id, "ClockOut"), CancellationToken.None);
        var syncItem = await _h.Db.SyncQueueEntries.SingleAsync();
        syncItem.Status = SyncStatus.Completed;
        await _h.Db.SaveChangesAsync();

        _h.Now = _h.Now.AddSeconds(20);
        await UndoHandler(user.Id).Handle(new UndoClockPunchCommand(punch.ClockEventId), CancellationToken.None);

        var original = await _h.Db.TimeEntries.AsNoTracking().SingleAsync(t => t.Id == entry.Id);
        original.TimerStop.Should().Be(punchedAt);
        original.DurationMinutes.Should().Be(90);
        var resumed = await _h.Db.TimeEntries.AsNoTracking().SingleAsync(t => t.Id != entry.Id);
        resumed.JobId.Should().Be(job.Id);
        resumed.TimerStart.Should().Be(punchedAt);
        resumed.TimerStop.Should().BeNull();
        (await _h.Db.SyncQueueEntries.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Handle_UndoneBreak_LeavesTheRunningTimerAlone()
    {
        var user = await _h.AddUserAsync();
        var entry = await _h.AddRunningTimerAsync(user.Id, null, _h.Now.AddMinutes(-30));
        var punch = await _punch.Handle(new ClockInOutCommand(user.Id, "BreakStart"), CancellationToken.None);

        await UndoHandler(user.Id).Handle(new UndoClockPunchCommand(punch.ClockEventId), CancellationToken.None);

        var saved = await _h.Db.TimeEntries.AsNoTracking().SingleAsync();
        saved.Id.Should().Be(entry.Id);
        saved.TimerStop.Should().BeNull();
        (await _h.Db.ActivityLogs.AnyAsync(a => a.Action == "timer-resumed")).Should().BeFalse();
    }
}
