using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.ShopFloor;
using Forge.Api.Features.TimeTracking;
using Forge.Core.Models;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.ShopFloor;

public class ClockTransitionRulesTests
{
    private readonly TimerTestHarness _h = new();
    private readonly ClockInOutHandler _handler;

    public ClockTransitionRulesTests()
    {
        _handler = new ClockInOutHandler(_h.Db, _h.ClockEventTypes.Object, _h.Mediator.Object, _h.Clock.Object);
    }

    private Task Punch(int userId, string code)
        => _handler.Handle(new ClockInOutCommand(userId, code), CancellationToken.None);

    [Fact]
    public async Task ClockIn_WhileAlreadyClockedIn_IsRefusedAndNothingIsRecorded()
    {
        var user = await _h.AddUserAsync();
        await _h.AddClockEventAsync(user.Id, "ClockIn", _h.Now.AddHours(-1));

        var act = () => Punch(user.Id, "ClockIn");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Clock In isn't available after Clock In*");
        (await _h.Db.ClockEvents.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ClockOut_WithNoPunchOnRecord_IsRefused()
    {
        var user = await _h.AddUserAsync();

        var act = () => Punch(user.Id, "ClockOut");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Clock Out isn't available while clocked out*");
        (await _h.Db.ClockEvents.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task ACustomEventWithNoPairedEvent_IsRecordedInAnyState()
    {
        var user = await _h.AddUserAsync();
        await _h.AddClockEventAsync(user.Id, "ClockIn", _h.Now.AddHours(-3));

        await Punch(user.Id, "toolbox_talk");
        await Punch(user.Id, "toolbox_talk");

        (await _h.Db.ClockEvents.CountAsync(e => e.EventTypeCode == "toolbox_talk")).Should().Be(2);
    }

    [Fact]
    public async Task LunchEnd_WhileOnBreak_IsRefused()
    {
        var user = await _h.AddUserAsync();
        await _h.AddClockEventAsync(user.Id, "ClockIn", _h.Now.AddHours(-3));
        await _h.AddClockEventAsync(user.Id, "BreakStart", _h.Now.AddMinutes(-5));

        var act = () => Punch(user.Id, "LunchEnd");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("End Lunch isn't available after Start Break*");
    }

    [Fact]
    public async Task BreakEnd_AfterBreakStart_IsRecorded()
    {
        var user = await _h.AddUserAsync();
        await _h.AddClockEventAsync(user.Id, "ClockIn", _h.Now.AddHours(-3));
        await _h.AddClockEventAsync(user.Id, "BreakStart", _h.Now.AddMinutes(-5));

        await Punch(user.Id, "BreakEnd");

        (await _h.Db.ClockEvents.CountAsync()).Should().Be(3);
    }

    [Fact]
    public async Task ClockOut_WhileOnBreak_IsRecorded()
    {
        var user = await _h.AddUserAsync();
        await _h.AddClockEventAsync(user.Id, "ClockIn", _h.Now.AddHours(-3));
        await _h.AddClockEventAsync(user.Id, "BreakStart", _h.Now.AddMinutes(-5));

        await Punch(user.Id, "ClockOut");

        (await _h.Db.ClockEvents.CountAsync()).Should().Be(3);
    }

    [Fact]
    public async Task ClockOut_OfAShiftLeftOpenFromAPriorDay_IsRecorded()
    {
        var user = await _h.AddUserAsync();
        await _h.AddClockEventAsync(user.Id, "ClockIn", _h.Now.AddHours(-30));

        await Punch(user.Id, "ClockOut");

        (await _h.Db.ClockEvents.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task ClockIn_AfterAClockInOlderThanTheBoardLookback_IsRecorded()
    {
        var user = await _h.AddUserAsync();
        await _h.AddClockEventAsync(user.Id, "ClockIn", _h.Now.AddHours(-(ClockStateRules.DefaultLookbackHours + 1)));

        await Punch(user.Id, "ClockIn");

        (await _h.Db.ClockEvents.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task DesktopClockIn_WhileAlreadyClockedIn_IsRefused()
    {
        var user = await _h.AddUserAsync();
        await _h.AddClockEventAsync(user.Id, "ClockIn", _h.Now.AddHours(-1));
        var handler = new CreateClockEventHandler(
            _h.Db, _h.SignedIn(user.Id), _h.ClockEventTypes.Object, _h.Mediator.Object, _h.Clock.Object);

        var act = () => handler.Handle(
            new CreateClockEventCommand(new CreateClockEventRequestModel("ClockIn", null, null, "desktop")),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await _h.Db.ClockEvents.CountAsync()).Should().Be(1);
    }

    private StartTimerHandler TimerHandler(int userId) => new(
        _h.Repo, _h.Jobs, _h.Db, _h.SignedIn(userId), _h.TimerHub.Object, _h.Mediator.Object, _h.Clock.Object,
        _h.ClockEventTypes.Object);

    [Fact]
    public async Task ClockOut_OfAShiftOlderThanTheBoardLookback_IsRecorded()
    {
        var user = await _h.AddUserAsync();
        await _h.AddClockEventAsync(user.Id, "ClockIn", _h.Now.AddHours(-(ClockStateRules.DefaultLookbackHours + 1)));

        await Punch(user.Id, "ClockOut");

        (await _h.Db.ClockEvents.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task LunchStart_ThenLunchEnd_AreRecorded()
    {
        var user = await _h.AddUserAsync();
        await _h.AddClockEventAsync(user.Id, "ClockIn", _h.Now.AddHours(-4));

        await Punch(user.Id, "LunchStart");
        _h.Now = _h.Now.AddMinutes(30);
        await Punch(user.Id, "LunchEnd");

        (await _h.Db.ClockEvents.CountAsync()).Should().Be(3);
    }

    [Fact]
    public async Task StartTimer_WhileClockedOut_IsRefused()
    {
        var user = await _h.AddUserAsync();
        await _h.AddClockEventAsync(user.Id, "ClockIn", _h.Now.AddHours(-3));
        await _h.AddClockEventAsync(user.Id, "ClockOut", _h.Now.AddHours(-1));

        var act = () => TimerHandler(user.Id).Handle(
            new StartTimerCommand(new StartTimerRequestModel(null, null, null)),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("You're clocked out*");
        (await _h.Db.TimeEntries.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task StartTimer_WhenClockedIn_Starts()
    {
        var user = await _h.AddUserAsync();
        await _h.AddClockEventAsync(user.Id, "ClockIn", _h.Now.AddHours(-1));

        await TimerHandler(user.Id).Handle(
            new StartTimerCommand(new StartTimerRequestModel(null, null, null)),
            CancellationToken.None);

        (await _h.Db.TimeEntries.CountAsync(t => t.UserId == user.Id)).Should().Be(1);
    }

    [Fact]
    public async Task StartTimer_OnBreak_Starts()
    {
        var user = await _h.AddUserAsync();
        await _h.AddClockEventAsync(user.Id, "ClockIn", _h.Now.AddHours(-3));
        await _h.AddClockEventAsync(user.Id, "BreakStart", _h.Now.AddMinutes(-5));

        await TimerHandler(user.Id).Handle(
            new StartTimerCommand(new StartTimerRequestModel(null, null, null)),
            CancellationToken.None);

        (await _h.Db.TimeEntries.CountAsync(t => t.UserId == user.Id)).Should().Be(1);
    }

    [Fact]
    public async Task StartTimer_ForAUserWhoNeverClocks_Starts()
    {
        var user = await _h.AddUserAsync();

        await TimerHandler(user.Id).Handle(
            new StartTimerCommand(new StartTimerRequestModel(null, null, null)),
            CancellationToken.None);

        (await _h.Db.TimeEntries.CountAsync(t => t.UserId == user.Id)).Should().Be(1);
    }

    [Fact]
    public async Task StartTimer_AfterAClockOutOlderThanTheBoardLookback_IsRefused()
    {
        var user = await _h.AddUserAsync();
        await _h.AddClockEventAsync(user.Id, "ClockIn", _h.Now.AddHours(-(ClockStateRules.DefaultLookbackHours + 9)));
        await _h.AddClockEventAsync(user.Id, "ClockOut", _h.Now.AddHours(-(ClockStateRules.DefaultLookbackHours + 1)));

        var act = () => TimerHandler(user.Id).Handle(
            new StartTimerCommand(new StartTimerRequestModel(null, null, null)),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("You're clocked out*");
        (await _h.Db.TimeEntries.AnyAsync()).Should().BeFalse();
    }
}
