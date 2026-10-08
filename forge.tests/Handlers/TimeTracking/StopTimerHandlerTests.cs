using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;
using Forge.Api.Features.TimeTracking;
using Forge.Core.Models;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.TimeTracking;

public class StopTimerHandlerTests
{
    private readonly TimerTestHarness _h = new();
    private readonly Mock<IHttpContextAccessor> _httpContext = new();
    private readonly StopTimerHandler _handler;
    private int _userId;

    public StopTimerHandlerTests()
    {
        _httpContext.Setup(h => h.HttpContext).Returns(() => new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, _userId.ToString())], "test")),
        });

        _handler = new StopTimerHandler(_h.Repo, _httpContext.Object, _h.Mediator.Object, _h.Clock.Object);
    }

    private async Task<int> SignInAsync()
    {
        _userId = (await _h.AddUserAsync()).Id;
        return _userId;
    }

    [Fact]
    public async Task Handle_ActiveTimer_StopsAndCalculatesDuration()
    {
        var userId = await SignInAsync();
        var job = await _h.AddJobAsync("JOB-0001");
        var entry = await _h.AddRunningTimerAsync(userId, job.Id, _h.Now.AddMinutes(-45));

        var result = await _handler.Handle(new StopTimerCommand(new StopTimerRequestModel(null)), CancellationToken.None);

        result.Id.Should().Be(entry.Id);
        result.JobNumber.Should().Be("JOB-0001");
        result.DurationMinutes.Should().Be(45);
        result.TimerStop.Should().Be(_h.Now);
    }

    [Fact]
    public async Task Handle_NoActiveTimer_ThrowsInvalidOperationException()
    {
        await SignInAsync();

        var act = () => _handler.Handle(new StopTimerCommand(new StopTimerRequestModel(null)), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No active timer*");
    }

    [Fact]
    public async Task Handle_WithNotes_UpdatesNotes()
    {
        var userId = await SignInAsync();
        var entry = await _h.AddRunningTimerAsync(userId, null, _h.Now.AddMinutes(-10));
        entry.Notes = "Original notes";
        await _h.Db.SaveChangesAsync();

        await _handler.Handle(new StopTimerCommand(new StopTimerRequestModel("  Updated notes ")), CancellationToken.None);

        var saved = await _h.Db.TimeEntries.AsNoTracking().SingleAsync(t => t.Id == entry.Id);
        saved.Notes.Should().Be("Updated notes");
    }

    [Fact]
    public async Task Handle_EmptyNotes_DoesNotOverwrite()
    {
        var userId = await SignInAsync();
        var entry = await _h.AddRunningTimerAsync(userId, null, _h.Now.AddMinutes(-10));
        entry.Notes = "Original notes";
        await _h.Db.SaveChangesAsync();

        await _handler.Handle(new StopTimerCommand(new StopTimerRequestModel("")), CancellationToken.None);

        var saved = await _h.Db.TimeEntries.AsNoTracking().SingleAsync(t => t.Id == entry.Id);
        saved.Notes.Should().Be("Original notes");
    }

    [Fact]
    public async Task Handle_BroadcastsTimerStoppedEvent()
    {
        var userId = await SignInAsync();
        await _h.AddRunningTimerAsync(userId, null, _h.Now.AddMinutes(-30));

        await _handler.Handle(new StopTimerCommand(new StopTimerRequestModel(null)), CancellationToken.None);

        _h.UserGroup.Verify(p => p.SendCoreAsync(
            "timerStopped",
            It.Is<object?[]>(args => args.Length == 1 && args[0] is TimerStoppedEvent),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ShortTimer_CalculatesZeroMinutes()
    {
        var userId = await SignInAsync();
        await _h.AddRunningTimerAsync(userId, null, _h.Now.AddSeconds(-29));

        var result = await _handler.Handle(new StopTimerCommand(new StopTimerRequestModel(null)), CancellationToken.None);

        result.DurationMinutes.Should().Be(0);
    }

    [Fact]
    public async Task Handle_EmptyBodyWithGeneralAndOperationTimers_StopsTheGeneralTimer()
    {
        var userId = await SignInAsync();
        var job = await _h.AddJobAsync("JOB-0101");
        var step = await _h.AddJobOperationAsync(job.Id, 20);
        var general = await _h.AddRunningTimerAsync(userId, job.Id, _h.Now.AddMinutes(-30));
        var operationTimer = await _h.AddRunningOperationTimerAsync(userId, step, _h.Now.AddMinutes(-10));

        var result = await _handler.Handle(new StopTimerCommand(new StopTimerRequestModel(null)), CancellationToken.None);

        result.Id.Should().Be(general.Id);
        (await _h.Db.TimeEntries.AsNoTracking().SingleAsync(t => t.Id == operationTimer.Id)).TimerStop.Should().BeNull();
    }

    [Fact]
    public async Task Handle_EmptyBodyWithOneOperationTimer_StopsIt()
    {
        var userId = await SignInAsync();
        var job = await _h.AddJobAsync("JOB-0102");
        var step = await _h.AddJobOperationAsync(job.Id, 20);
        var operationTimer = await _h.AddRunningOperationTimerAsync(userId, step, _h.Now.AddMinutes(-10));

        var result = await _handler.Handle(new StopTimerCommand(new StopTimerRequestModel(null)), CancellationToken.None);

        result.Id.Should().Be(operationTimer.Id);
        result.DurationMinutes.Should().Be(10);
    }

    [Fact]
    public async Task Handle_EmptyBodyWithTwoOperationTimers_AsksWhichOne()
    {
        var userId = await SignInAsync();
        var job = await _h.AddJobAsync("JOB-0103");
        await _h.AddRunningOperationTimerAsync(userId, await _h.AddJobOperationAsync(job.Id, 20), _h.Now.AddMinutes(-10));
        await _h.AddRunningOperationTimerAsync(userId, await _h.AddJobOperationAsync(job.Id, 30), _h.Now.AddMinutes(-5));

        var act = () => _handler.Handle(new StopTimerCommand(new StopTimerRequestModel(null)), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Several timers are running*");
    }

    [Fact]
    public async Task Handle_JobIdGiven_StopsOnlyThatJobsTimer()
    {
        var userId = await SignInAsync();
        var first = await _h.AddJobAsync("JOB-0104");
        var second = await _h.AddJobAsync("JOB-0105");
        await _h.AddRunningOperationTimerAsync(userId, await _h.AddJobOperationAsync(first.Id, 20), _h.Now.AddMinutes(-10));
        var target = await _h.AddRunningOperationTimerAsync(
            userId, await _h.AddJobOperationAsync(second.Id, 20), _h.Now.AddMinutes(-5));

        var result = await _handler.Handle(
            new StopTimerCommand(new StopTimerRequestModel(null, JobId: second.Id)), CancellationToken.None);

        result.Id.Should().Be(target.Id);
        (await _h.Db.TimeEntries.CountAsync(t => t.TimerStop == null)).Should().Be(1);
    }

    [Fact]
    public async Task Handle_TimeEntryIdOfAnotherUser_IsNotFound()
    {
        var other = await _h.AddUserAsync();
        var entry = await _h.AddRunningTimerAsync(other.Id, null, _h.Now.AddMinutes(-10));
        await SignInAsync();

        var act = () => _handler.Handle(
            new StopTimerCommand(new StopTimerRequestModel(null, TimeEntryId: entry.Id)), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        (await _h.Db.TimeEntries.AsNoTracking().SingleAsync(t => t.Id == entry.Id)).TimerStop.Should().BeNull();
    }

    [Fact]
    public async Task Handle_TimeEntryIdAlreadyStopped_ReturnsItUnchanged()
    {
        var userId = await SignInAsync();
        var entry = await _h.AddRunningTimerAsync(userId, null, _h.Now.AddMinutes(-60));
        var stoppedAt = _h.Now.AddMinutes(-30);
        entry.TimerStop = stoppedAt;
        entry.DurationMinutes = 30;
        await _h.Db.SaveChangesAsync();

        var result = await _handler.Handle(
            new StopTimerCommand(new StopTimerRequestModel("ignored", TimeEntryId: entry.Id)), CancellationToken.None);

        result.TimerStop.Should().Be(stoppedAt);
        result.DurationMinutes.Should().Be(30);
        result.Notes.Should().BeNull();
    }

    [Fact]
    public async Task Handle_TimeEntryId_StopsThatTimerAndLeavesTheRest()
    {
        var userId = await SignInAsync();
        var job = await _h.AddJobAsync("JOB-0106");
        var general = await _h.AddRunningTimerAsync(userId, job.Id, _h.Now.AddMinutes(-30));
        var operationTimer = await _h.AddRunningOperationTimerAsync(
            userId, await _h.AddJobOperationAsync(job.Id, 20), _h.Now.AddMinutes(-10));

        var result = await _handler.Handle(
            new StopTimerCommand(new StopTimerRequestModel(null, TimeEntryId: operationTimer.Id)), CancellationToken.None);

        result.Id.Should().Be(operationTimer.Id);
        result.JobOperationId.Should().NotBeNull();
        (await _h.Db.TimeEntries.AsNoTracking().SingleAsync(t => t.Id == general.Id)).TimerStop.Should().BeNull();
    }
}
