using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Moq;
using Forge.Api.Features.TimeTracking;
using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Tests.Handlers.TimeTracking;

public class StartTimerHandlerTests
{
    private readonly Mock<ITimeTrackingRepository> _repo = new();
    private readonly Mock<IHttpContextAccessor> _httpContext = new();
    private readonly Mock<IHubContext<TimerHub>> _timerHub = new();
    private readonly StartTimerHandler _handler;

    private const int TestUserId = 42;

    public StartTimerHandlerTests()
    {
        SetupHttpContext(TestUserId);
        SetupHubMock();
        _handler = new StartTimerHandler(_repo.Object, _httpContext.Object, _timerHub.Object);
    }

    private void SetupHttpContext(int userId)
    {
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) };
        var identity = new ClaimsIdentity(claims, "test");
        var principal = new ClaimsPrincipal(identity);
        var httpContext = new DefaultHttpContext { User = principal };
        _httpContext.Setup(h => h.HttpContext).Returns(httpContext);
    }

    private void SetupHubMock()
    {
        var mockClients = new Mock<IHubClients>();
        var mockClientProxy = new Mock<IClientProxy>();
        mockClients.Setup(c => c.Group(It.IsAny<string>())).Returns(mockClientProxy.Object);
        _timerHub.Setup(h => h.Clients).Returns(mockClients.Object);
    }

    [Fact]
    public async Task Handle_NoActiveTimer_CreatesNewTimerEntry()
    {
        // Arrange
        _repo.Setup(r => r.GetActiveTimerAsync(TestUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TimeEntry?)null);

        var expectedResult = new TimeEntryResponseModel
        {
            Id = 1, JobId = 5, JobNumber = "JOB-0001", UserId = TestUserId, UserName = "John Doe",
            Date = DateOnly.FromDateTime(DateTime.UtcNow), DurationMinutes = 0, Category = "Machining",
            Notes = "Working on widget", TimerStart = DateTime.UtcNow, TimerStop = null,
            IsManual = false, IsLocked = false, CreatedAt = DateTime.UtcNow,
        };

        _repo.Setup(r => r.GetTimeEntryByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var request = new StartTimerRequestModel(5, "Machining", "Working on widget");
        var command = new StartTimerCommand(request);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.UserId.Should().Be(TestUserId);

        _repo.Verify(r => r.AddTimeEntryAsync(It.Is<TimeEntry>(e =>
            e.UserId == TestUserId &&
            e.JobId == 5 &&
            e.DurationMinutes == 0 &&
            e.Category == "Machining" &&
            e.Notes == "Working on widget" &&
            e.TimerStart != null &&
            e.IsManual == false
        ), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ActiveTimerExists_ThrowsInvalidOperationException()
    {
        // Arrange
        var activeEntry = new TimeEntry
        {
            Id = 10,
            UserId = TestUserId,
            TimerStart = DateTime.UtcNow.AddHours(-1),
        };

        _repo.Setup(r => r.GetActiveTimerAsync(TestUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(activeEntry);

        var request = new StartTimerRequestModel(5, null, null);
        var command = new StartTimerCommand(request);

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already running*");
    }

    [Fact]
    public async Task Handle_NullJobId_CreatesEntryWithoutJob()
    {
        // Arrange
        _repo.Setup(r => r.GetActiveTimerAsync(TestUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TimeEntry?)null);

        var expectedResult = new TimeEntryResponseModel
        {
            Id = 1, JobId = null, JobNumber = null, UserId = TestUserId, UserName = "John Doe",
            Date = DateOnly.FromDateTime(DateTime.UtcNow), DurationMinutes = 0, Category = null,
            Notes = null, TimerStart = DateTime.UtcNow, TimerStop = null,
            IsManual = false, IsLocked = false, CreatedAt = DateTime.UtcNow,
        };

        _repo.Setup(r => r.GetTimeEntryByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var request = new StartTimerRequestModel(null, null, null);
        var command = new StartTimerCommand(request);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _repo.Verify(r => r.AddTimeEntryAsync(It.Is<TimeEntry>(e =>
            e.JobId == null
        ), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_TrimsNotes()
    {
        // Arrange
        _repo.Setup(r => r.GetActiveTimerAsync(TestUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TimeEntry?)null);

        var expectedResult = new TimeEntryResponseModel
        {
            Id = 1, JobId = null, JobNumber = null, UserId = TestUserId, UserName = "John Doe",
            Date = DateOnly.FromDateTime(DateTime.UtcNow), DurationMinutes = 0, Category = "Setup",
            Notes = "Trimmed notes", TimerStart = DateTime.UtcNow, TimerStop = null,
            IsManual = false, IsLocked = false, CreatedAt = DateTime.UtcNow,
        };

        _repo.Setup(r => r.GetTimeEntryByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var request = new StartTimerRequestModel(null, "  Setup  ", "  Trimmed notes  ");
        var command = new StartTimerCommand(request);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _repo.Verify(r => r.AddTimeEntryAsync(It.Is<TimeEntry>(e =>
            e.Category == "Setup" &&
            e.Notes == "Trimmed notes"
        ), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_BroadcastsTimerStartedEvent()
    {
        // Arrange
        _repo.Setup(r => r.GetActiveTimerAsync(TestUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TimeEntry?)null);

        var expectedResult = new TimeEntryResponseModel
        {
            Id = 1, JobId = null, JobNumber = null, UserId = TestUserId, UserName = "John Doe",
            Date = DateOnly.FromDateTime(DateTime.UtcNow), DurationMinutes = 0, Category = null,
            Notes = null, TimerStart = DateTime.UtcNow, TimerStop = null,
            IsManual = false, IsLocked = false, CreatedAt = DateTime.UtcNow,
        };

        _repo.Setup(r => r.GetTimeEntryByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var mockClientProxy = new Mock<IClientProxy>();
        var mockClients = new Mock<IHubClients>();
        mockClients.Setup(c => c.Group($"user:{TestUserId}")).Returns(mockClientProxy.Object);
        _timerHub.Setup(h => h.Clients).Returns(mockClients.Object);

        var request = new StartTimerRequestModel(null, null, null);
        var command = new StartTimerCommand(request);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        mockClientProxy.Verify(p => p.SendCoreAsync(
            "timerStarted",
            It.Is<object?[]>(args => args.Length == 1 && args[0] is TimerStartedEvent),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
