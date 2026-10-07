using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;
using Forge.Api.Features.TimeTracking;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.TimeTracking;

public class StartTimerHandlerTests
{
    private readonly TimerTestHarness _h = new();
    private readonly Mock<IHttpContextAccessor> _httpContext = new();
    private readonly StartTimerHandler _handler;
    private int _userId;

    public StartTimerHandlerTests()
    {
        _httpContext.Setup(h => h.HttpContext).Returns(() => new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, _userId.ToString())], "test")),
        });

        _handler = new StartTimerHandler(
            _h.Repo, _h.Jobs, _httpContext.Object, _h.TimerHub.Object, _h.Mediator.Object, _h.Clock.Object);
    }

    private async Task<int> SignInAsync()
    {
        _userId = (await _h.AddUserAsync()).Id;
        return _userId;
    }

    [Fact]
    public async Task Handle_NoActiveTimer_CreatesNewTimerEntry()
    {
        var userId = await SignInAsync();
        var job = await _h.AddJobAsync("JOB-0001");

        var result = await _handler.Handle(
            new StartTimerCommand(new StartTimerRequestModel(job.Id, "Machining", "Working on widget")),
            CancellationToken.None);

        result.UserId.Should().Be(userId);
        var entry = await _h.Db.TimeEntries.AsNoTracking().SingleAsync();
        entry.JobId.Should().Be(job.Id);
        entry.DurationMinutes.Should().Be(0);
        entry.Category.Should().Be("Machining");
        entry.Notes.Should().Be("Working on widget");
        entry.TimerStart.Should().Be(_h.Now);
        entry.TimerStop.Should().BeNull();
        entry.IsManual.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ActiveTimerExists_ThrowsWithItsJobNumber()
    {
        var userId = await SignInAsync();
        var running = await _h.AddJobAsync("JOB-0007");
        var next = await _h.AddJobAsync("JOB-0008");
        await _h.AddRunningTimerAsync(userId, running.Id, _h.Now.AddHours(-1));

        var act = () => _handler.Handle(
            new StartTimerCommand(new StartTimerRequestModel(next.Id, null, null)), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("You're already running a timer on JOB-0007. Stop it first.");
        (await _h.Db.TimeEntries.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Handle_ActiveTimerWithoutJob_ThrowsAlreadyRunning()
    {
        var userId = await SignInAsync();
        await _h.AddRunningTimerAsync(userId, null, _h.Now.AddHours(-1));

        var act = () => _handler.Handle(
            new StartTimerCommand(new StartTimerRequestModel(null, null, null)), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already running*Stop it first.");
    }

    [Fact]
    public async Task Handle_ArchivedJob_IsRejected()
    {
        await SignInAsync();
        var job = await _h.AddJobAsync("JOB-0009", isArchived: true);

        var act = () => _handler.Handle(
            new StartTimerCommand(new StartTimerRequestModel(job.Id, null, null)), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("This work order is closed and can't take time.");
        (await _h.Db.TimeEntries.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Handle_DisposedJob_IsRejected()
    {
        await SignInAsync();
        var job = await _h.AddJobAsync("JOB-0010", disposition: JobDisposition.ShipToCustomer);

        var act = () => _handler.Handle(
            new StartTimerCommand(new StartTimerRequestModel(job.Id, null, null)), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("This work order is closed and can't take time.");
    }

    [Fact]
    public async Task Handle_SwitchFromActive_ClosesOldTimerAndOpensNewOneWithNoGap()
    {
        var userId = await SignInAsync();
        var oldJob = await _h.AddJobAsync("JOB-0020");
        var newJob = await _h.AddJobAsync("JOB-0021");
        var old = await _h.AddRunningTimerAsync(userId, oldJob.Id, _h.Now.AddMinutes(-20));

        var result = await _handler.Handle(
            new StartTimerCommand(new StartTimerRequestModel(newJob.Id, null, null, SwitchFromActive: true)),
            CancellationToken.None);

        var closed = await _h.Db.TimeEntries.AsNoTracking().SingleAsync(t => t.Id == old.Id);
        closed.TimerStop.Should().Be(_h.Now);
        closed.DurationMinutes.Should().Be(20);
        result.JobId.Should().Be(newJob.Id);
        result.TimerStart.Should().Be(closed.TimerStop);
        result.TimerStop.Should().BeNull();
        (await _h.Db.TimeEntries.CountAsync(t => t.UserId == userId && t.TimerStop == null)).Should().Be(1);
    }

    [Fact]
    public async Task Handle_SwitchFromActive_WithNoRunningTimer_JustStarts()
    {
        await SignInAsync();

        var result = await _handler.Handle(
            new StartTimerCommand(new StartTimerRequestModel(null, null, null, SwitchFromActive: true)),
            CancellationToken.None);

        result.TimerStart.Should().Be(_h.Now);
        _h.Mediator.Verify(m => m.Send(It.IsAny<StopActiveTimerCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_TrimsNotes()
    {
        await SignInAsync();

        await _handler.Handle(
            new StartTimerCommand(new StartTimerRequestModel(null, "  Setup  ", "  Trimmed notes  ")),
            CancellationToken.None);

        var entry = await _h.Db.TimeEntries.AsNoTracking().SingleAsync();
        entry.Category.Should().Be("Setup");
        entry.Notes.Should().Be("Trimmed notes");
        entry.JobId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_BroadcastsTimerStartedEvent()
    {
        await SignInAsync();

        await _handler.Handle(new StartTimerCommand(new StartTimerRequestModel(null, null, null)), CancellationToken.None);

        _h.UserGroup.Verify(p => p.SendCoreAsync(
            "timerStarted",
            It.Is<object?[]>(args => args.Length == 1 && args[0] is TimerStartedEvent),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
