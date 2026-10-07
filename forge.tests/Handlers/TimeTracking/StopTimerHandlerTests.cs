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
}
