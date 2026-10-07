using FluentAssertions;
using Moq;

using Forge.Api.Features.TimeTracking;
using Forge.Core.Entities;
using Forge.Core.Interfaces;

namespace Forge.Tests.Handlers.TimeTracking;

public class GetActiveTimerHandlerTests
{
    private const int TestUserId = 42;

    private readonly Mock<ITimeTrackingRepository> _repo = new();
    private readonly Mock<IJobRepository> _jobs = new();
    private readonly GetActiveTimerHandler _handler;

    public GetActiveTimerHandlerTests()
    {
        _handler = new GetActiveTimerHandler(_repo.Object, _jobs.Object);
    }

    [Fact]
    public async Task Handle_NoRunningTimer_ReturnsNull()
    {
        _repo.Setup(r => r.GetActiveTimerAsync(TestUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TimeEntry?)null);

        var result = await _handler.Handle(new GetActiveTimerQuery(TestUserId), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Handle_TimerOnJob_ReturnsEntryWithJobNumber()
    {
        var start = new DateTimeOffset(2026, 10, 7, 14, 30, 0, TimeSpan.Zero);
        _repo.Setup(r => r.GetActiveTimerAsync(TestUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TimeEntry { Id = 7, UserId = TestUserId, JobId = 5, OperationId = 11, TimerStart = start });
        _jobs.Setup(j => j.FindAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Job { Id = 5, JobNumber = "J-1042" });

        var result = await _handler.Handle(new GetActiveTimerQuery(TestUserId), CancellationToken.None);

        result.Should().NotBeNull();
        result!.TimeEntryId.Should().Be(7);
        result.JobId.Should().Be(5);
        result.JobNumber.Should().Be("J-1042");
        result.OperationId.Should().Be(11);
        result.TimerStart.Should().Be(start);
    }

    [Fact]
    public async Task Handle_TimerWithoutJob_ReturnsEntryWithoutJobNumber()
    {
        var start = new DateTimeOffset(2026, 10, 7, 8, 0, 0, TimeSpan.Zero);
        _repo.Setup(r => r.GetActiveTimerAsync(TestUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TimeEntry { Id = 9, UserId = TestUserId, TimerStart = start });

        var result = await _handler.Handle(new GetActiveTimerQuery(TestUserId), CancellationToken.None);

        result.Should().NotBeNull();
        result!.JobId.Should().BeNull();
        result.JobNumber.Should().BeNull();
        _jobs.Verify(j => j.FindAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_QueriesTheRequestedUser()
    {
        _repo.Setup(r => r.GetActiveTimerAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TimeEntry?)null);

        await _handler.Handle(new GetActiveTimerQuery(99), CancellationToken.None);

        _repo.Verify(r => r.GetActiveTimerAsync(99, It.IsAny<CancellationToken>()), Times.Once);
    }
}
