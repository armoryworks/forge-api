using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;
using Forge.Api.Features.TimeTracking;
using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Tests.Handlers.TimeTracking;

public class StopTimerHandlerTests
{
    private readonly Mock<ITimeTrackingRepository> _repo = new();
    private readonly Mock<IHttpContextAccessor> _httpContext = new();
    private readonly Mock<IHubContext<TimerHub>> _timerHub = new();
    private readonly StopTimerHandler _handler;

    private const int TestUserId = 42;

    public StopTimerHandlerTests()
    {
        SetupHttpContext(TestUserId);
        SetupHubMock();
        var userManagerMock = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(),
            null!, null!, null!, null!, null!, null!, null!, null!);

        _handler = new StopTimerHandler(
            _repo.Object,
            _httpContext.Object,
            _timerHub.Object,
            Mock.Of<ISyncQueueRepository>(),
            Mock.Of<IAccountingProviderFactory>(),
            userManagerMock.Object,
            Mock.Of<IJobRepository>(),
            Mock.Of<ICustomerRepository>(),
            Mock.Of<ILogger<StopTimerHandler>>());
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
    public async Task Handle_ActiveTimer_StopsAndCalculatesDuration()
    {
        // Arrange
        var timerStart = DateTime.UtcNow.AddMinutes(-45);
        var activeEntry = new TimeEntry
        {
            Id = 10,
            UserId = TestUserId,
            JobId = 5,
            Date = DateOnly.FromDateTime(timerStart),
            TimerStart = timerStart,
            DurationMinutes = 0,
            IsManual = false,
        };

        _repo.Setup(r => r.GetActiveTimerAsync(TestUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(activeEntry);

        var expectedResult = new TimeEntryResponseModel
        {
            Id = 10, JobId = 5, JobNumber = "JOB-0001", UserId = TestUserId, UserName = "John Doe",
            Date = DateOnly.FromDateTime(timerStart), DurationMinutes = 45, Category = null,
            Notes = null, TimerStart = timerStart, TimerStop = DateTime.UtcNow,
            IsManual = false, IsLocked = false, CreatedAt = DateTime.UtcNow,
        };

        _repo.Setup(r => r.GetTimeEntryByIdAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var request = new StopTimerRequestModel(null);
        var command = new StopTimerCommand(request);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(10);

        activeEntry.TimerStop.Should().NotBeNull();
        activeEntry.DurationMinutes.Should().BeGreaterThanOrEqualTo(44); // Allow for test execution time
        activeEntry.DurationMinutes.Should().BeLessThanOrEqualTo(46);

        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NoActiveTimer_ThrowsInvalidOperationException()
    {
        // Arrange
        _repo.Setup(r => r.GetActiveTimerAsync(TestUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TimeEntry?)null);

        var request = new StopTimerRequestModel(null);
        var command = new StopTimerCommand(request);

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No active timer*");
    }

    [Fact]
    public async Task Handle_WithNotes_UpdatesNotes()
    {
        // Arrange
        var timerStart = DateTime.UtcNow.AddMinutes(-10);
        var activeEntry = new TimeEntry
        {
            Id = 10,
            UserId = TestUserId,
            TimerStart = timerStart,
            Notes = "Original notes",
        };

        _repo.Setup(r => r.GetActiveTimerAsync(TestUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(activeEntry);

        var expectedResult = new TimeEntryResponseModel
        {
            Id = 10, JobId = null, JobNumber = null, UserId = TestUserId, UserName = "John Doe",
            Date = DateOnly.FromDateTime(timerStart), DurationMinutes = 10, Category = null,
            Notes = "Updated notes", TimerStart = timerStart, TimerStop = DateTime.UtcNow,
            IsManual = false, IsLocked = false, CreatedAt = DateTime.UtcNow,
        };

        _repo.Setup(r => r.GetTimeEntryByIdAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var request = new StopTimerRequestModel("Updated notes");
        var command = new StopTimerCommand(request);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        activeEntry.Notes.Should().Be("Updated notes");
    }

    [Fact]
    public async Task Handle_EmptyNotes_DoesNotOverwrite()
    {
        // Arrange
        var timerStart = DateTime.UtcNow.AddMinutes(-10);
        var activeEntry = new TimeEntry
        {
            Id = 10,
            UserId = TestUserId,
            TimerStart = timerStart,
            Notes = "Original notes",
        };

        _repo.Setup(r => r.GetActiveTimerAsync(TestUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(activeEntry);

        var expectedResult = new TimeEntryResponseModel
        {
            Id = 10, JobId = null, JobNumber = null, UserId = TestUserId, UserName = "John Doe",
            Date = DateOnly.FromDateTime(timerStart), DurationMinutes = 10, Category = null,
            Notes = "Original notes", TimerStart = timerStart, TimerStop = DateTime.UtcNow,
            IsManual = false, IsLocked = false, CreatedAt = DateTime.UtcNow,
        };

        _repo.Setup(r => r.GetTimeEntryByIdAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var request = new StopTimerRequestModel("");
        var command = new StopTimerCommand(request);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        activeEntry.Notes.Should().Be("Original notes");
    }

    [Fact]
    public async Task Handle_BroadcastsTimerStoppedEvent()
    {
        // Arrange
        var timerStart = DateTime.UtcNow.AddMinutes(-30);
        var activeEntry = new TimeEntry
        {
            Id = 10,
            UserId = TestUserId,
            TimerStart = timerStart,
        };

        _repo.Setup(r => r.GetActiveTimerAsync(TestUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(activeEntry);

        var expectedResult = new TimeEntryResponseModel
        {
            Id = 10, JobId = null, JobNumber = null, UserId = TestUserId, UserName = "John Doe",
            Date = DateOnly.FromDateTime(timerStart), DurationMinutes = 30, Category = null,
            Notes = null, TimerStart = timerStart, TimerStop = DateTime.UtcNow,
            IsManual = false, IsLocked = false, CreatedAt = DateTime.UtcNow,
        };

        _repo.Setup(r => r.GetTimeEntryByIdAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var mockClientProxy = new Mock<IClientProxy>();
        var mockClients = new Mock<IHubClients>();
        mockClients.Setup(c => c.Group($"user:{TestUserId}")).Returns(mockClientProxy.Object);
        _timerHub.Setup(h => h.Clients).Returns(mockClients.Object);

        var request = new StopTimerRequestModel(null);
        var command = new StopTimerCommand(request);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        mockClientProxy.Verify(p => p.SendCoreAsync(
            "timerStopped",
            It.Is<object?[]>(args => args.Length == 1 && args[0] is TimerStoppedEvent),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ShortTimer_CalculatesZeroMinutes()
    {
        // Arrange - timer started less than a minute ago
        var timerStart = DateTime.UtcNow.AddSeconds(-30);
        var activeEntry = new TimeEntry
        {
            Id = 10,
            UserId = TestUserId,
            TimerStart = timerStart,
        };

        _repo.Setup(r => r.GetActiveTimerAsync(TestUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(activeEntry);

        var expectedResult = new TimeEntryResponseModel
        {
            Id = 10, JobId = null, JobNumber = null, UserId = TestUserId, UserName = "John Doe",
            Date = DateOnly.FromDateTime(timerStart), DurationMinutes = 0, Category = null,
            Notes = null, TimerStart = timerStart, TimerStop = DateTime.UtcNow,
            IsManual = false, IsLocked = false, CreatedAt = DateTime.UtcNow,
        };

        _repo.Setup(r => r.GetTimeEntryByIdAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var request = new StopTimerRequestModel(null);
        var command = new StopTimerCommand(request);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        activeEntry.DurationMinutes.Should().Be(0);
    }
}
