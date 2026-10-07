using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.ShopFloor;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.ShopFloor;

public class ClockInOutTimerTests
{
    private readonly TimerTestHarness _h = new();
    private readonly ClockInOutHandler _handler;

    public ClockInOutTimerTests()
    {
        var types = new Mock<IClockEventTypeService>();
        var definitions = new List<ClockEventTypeDefinition>
        {
            new("ClockIn", "Clock In", "In", "ClockOut", "work", true, true, "login", "#22c55e"),
            new("ClockOut", "Clock Out", "Out", "ClockIn", "work", false, false, "logout", "#ef4444"),
            new("BreakStart", "Start Break", "OnBreak", "BreakEnd", "break", true, true, "free_breakfast", "#f59e0b"),
            new("LunchStart", "Start Lunch", "OnLunch", "LunchEnd", "lunch", true, true, "restaurant", "#f97316"),
            new("shift_end", "End Shift", "Out", "ClockIn", "work", false, false, "logout", "#ef4444"),
            new("smoke_break", "Smoke Break", "OnBreak", "ClockIn", "break", true, true, "pause", "#f59e0b"),
            new("return_from_errand", "Back", "In", null, "work", true, false, "login", "#22c55e"),
        };
        types.Setup(t => t.GetByCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string code, CancellationToken _) => definitions.FirstOrDefault(d => d.Code == code));

        _handler = new ClockInOutHandler(_h.Db, types.Object, _h.Mediator.Object, _h.Clock.Object);
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
}
