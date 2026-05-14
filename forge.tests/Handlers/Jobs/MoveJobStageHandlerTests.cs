using Bogus;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;
using Forge.Api.Features.Jobs;
using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Integrations;

namespace Forge.Tests.Handlers.Jobs;

public class MoveJobStageHandlerTests
{
    private readonly Mock<IJobRepository> _jobRepo = new();
    private readonly Mock<ITrackTypeRepository> _trackRepo = new();
    private readonly Mock<IActivityLogRepository> _actRepo = new();
    private readonly Mock<IMediator> _mediator = new();
    private readonly Mock<IHubContext<BoardHub>> _boardHub = new();
    private readonly MoveJobStageHandler _handler;

    private readonly Faker _faker = new();

    public MoveJobStageHandlerTests()
    {
        var mockClients = new Mock<IHubClients>();
        var mockClientProxy = new Mock<IClientProxy>();
        mockClients.Setup(c => c.Group(It.IsAny<string>())).Returns(mockClientProxy.Object);
        _boardHub.Setup(h => h.Clients).Returns(mockClients.Object);

        _handler = new MoveJobStageHandler(
            _jobRepo.Object,
            _trackRepo.Object,
            _actRepo.Object,
            Mock.Of<ICustomerRepository>(),
            Mock.Of<IAccountingService>(),
            Mock.Of<ISyncQueueRepository>(),
            Mock.Of<IWorkCenterContext>(),
            _mediator.Object,
            _boardHub.Object,
            Mock.Of<Microsoft.AspNetCore.Http.IHttpContextAccessor>(),
            new SystemClock(),
            Mock.Of<ILogger<MoveJobStageHandler>>());
    }

    [Fact]
    public async Task Handle_ValidMove_UpdatesStageAndPosition()
    {
        // Arrange
        var trackTypeId = 1;
        var fromStageId = 5;
        var toStageId = 6;

        var job = new Job
        {
            Id = 10,
            JobNumber = "JOB-0001",
            Title = "Test Job",
            TrackTypeId = trackTypeId,
            CurrentStageId = fromStageId,
            BoardPosition = 3,
        };

        var fromStage = new JobStage { Id = fromStageId, TrackTypeId = trackTypeId, Name = "Quoted", SortOrder = 1 };
        var toStage = new JobStage { Id = toStageId, TrackTypeId = trackTypeId, Name = "Order Confirmed", SortOrder = 2 };

        _jobRepo.Setup(r => r.FindAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(job);
        _trackRepo.Setup(r => r.FindStageAsync(toStageId, It.IsAny<CancellationToken>())).ReturnsAsync(toStage);
        _trackRepo.Setup(r => r.FindStageAsync(fromStageId, It.IsAny<CancellationToken>())).ReturnsAsync(fromStage);
        _trackRepo.Setup(r => r.GetStagesByTrackTypeAsync(trackTypeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<JobStage> { fromStage, toStage });
        _jobRepo.Setup(r => r.GetMaxBoardPositionAsync(toStageId, It.IsAny<CancellationToken>())).ReturnsAsync(7);

        var expectedResult = new JobDetailResponseModel(
            10, "JOB-0001", "Test Job", null, trackTypeId, "Production",
            toStageId, "Order Confirmed", "#22c55e", null, null, null, null,
            "Normal", null, null, null, null, null, false, 8, 0, null,
            null, null, null, null, null, null, null, null, null, null, 0,
            DateTime.UtcNow, DateTime.UtcNow);

        _mediator.Setup(m => m.Send(It.IsAny<GetJobByIdQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var command = new MoveJobStageCommand(10, toStageId);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        job.CurrentStageId.Should().Be(toStageId);
        job.BoardPosition.Should().Be(8);
        result.StageName.Should().Be("Order Confirmed");
        _jobRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_JobNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        _jobRepo.Setup(r => r.FindAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Job?)null);

        var command = new MoveJobStageCommand(999, 1);

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*999*");
    }

    [Fact]
    public async Task Handle_StageNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        var job = new Job { Id = 1, TrackTypeId = 1, CurrentStageId = 5 };
        _jobRepo.Setup(r => r.FindAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(job);
        _trackRepo.Setup(r => r.FindStageAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync((JobStage?)null);

        var command = new MoveJobStageCommand(1, 999);

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*999*");
    }

    [Fact]
    public async Task Handle_StageBelongsToDifferentTrack_ThrowsInvalidOperationException()
    {
        // Arrange
        var job = new Job { Id = 1, TrackTypeId = 1, CurrentStageId = 5 };
        var wrongTrackStage = new JobStage { Id = 20, TrackTypeId = 2, Name = "Wrong Track Stage" };

        _jobRepo.Setup(r => r.FindAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(job);
        _trackRepo.Setup(r => r.FindStageAsync(20, It.IsAny<CancellationToken>())).ReturnsAsync(wrongTrackStage);

        var command = new MoveJobStageCommand(1, 20);

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*does not belong to track type*");
    }

    [Fact]
    public async Task Handle_CreatesActivityLogWithStageNames()
    {
        // Arrange
        var trackTypeId = 1;
        var fromStageId = 5;
        var toStageId = 6;

        var job = new Job { Id = 1, TrackTypeId = trackTypeId, CurrentStageId = fromStageId };
        var fromStage = new JobStage { Id = fromStageId, TrackTypeId = trackTypeId, Name = "Materials Ordered", SortOrder = 3 };
        var toStage = new JobStage { Id = toStageId, TrackTypeId = trackTypeId, Name = "Materials Received", SortOrder = 4 };

        _jobRepo.Setup(r => r.FindAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(job);
        _trackRepo.Setup(r => r.FindStageAsync(toStageId, It.IsAny<CancellationToken>())).ReturnsAsync(toStage);
        _trackRepo.Setup(r => r.FindStageAsync(fromStageId, It.IsAny<CancellationToken>())).ReturnsAsync(fromStage);
        _trackRepo.Setup(r => r.GetStagesByTrackTypeAsync(trackTypeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<JobStage> { fromStage, toStage });
        _jobRepo.Setup(r => r.GetMaxBoardPositionAsync(toStageId, It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var expectedResult = new JobDetailResponseModel(
            1, "JOB-0001", "Test", null, trackTypeId, "Production",
            toStageId, "Materials Received", "#22c55e", null, null, null, null,
            "Normal", null, null, null, null, null, false, 1, 0, null,
            null, null, null, null, null, null, null, null, null, null, 0,
            DateTime.UtcNow, DateTime.UtcNow);

        _mediator.Setup(m => m.Send(It.IsAny<GetJobByIdQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var command = new MoveJobStageCommand(1, toStageId);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _actRepo.Verify(r => r.AddAsync(It.Is<JobActivityLog>(log =>
            log.Action == ActivityAction.StageMoved &&
            log.OldValue == "Materials Ordered" &&
            log.NewValue == "Materials Received" &&
            log.Description!.Contains("Materials Ordered") &&
            log.Description!.Contains("Materials Received")
        ), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_BroadcastsJobMovedEvent()
    {
        // Arrange
        var trackTypeId = 1;
        var fromStageId = 5;
        var toStageId = 6;

        var job = new Job { Id = 1, TrackTypeId = trackTypeId, CurrentStageId = fromStageId };
        var fromStage = new JobStage { Id = fromStageId, TrackTypeId = trackTypeId, Name = "Quoted", SortOrder = 1 };
        var toStage = new JobStage { Id = toStageId, TrackTypeId = trackTypeId, Name = "Order Confirmed", SortOrder = 2 };

        _jobRepo.Setup(r => r.FindAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(job);
        _trackRepo.Setup(r => r.FindStageAsync(toStageId, It.IsAny<CancellationToken>())).ReturnsAsync(toStage);
        _trackRepo.Setup(r => r.FindStageAsync(fromStageId, It.IsAny<CancellationToken>())).ReturnsAsync(fromStage);
        _trackRepo.Setup(r => r.GetStagesByTrackTypeAsync(trackTypeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<JobStage> { fromStage, toStage });
        _jobRepo.Setup(r => r.GetMaxBoardPositionAsync(toStageId, It.IsAny<CancellationToken>())).ReturnsAsync(2);

        var expectedResult = new JobDetailResponseModel(
            1, "JOB-0001", "Test", null, trackTypeId, "Production",
            toStageId, "Order Confirmed", "#22c55e", null, null, null, null,
            "Normal", null, null, null, null, null, false, 3, 0, null,
            null, null, null, null, null, null, null, null, null, null, 0,
            DateTime.UtcNow, DateTime.UtcNow);

        _mediator.Setup(m => m.Send(It.IsAny<GetJobByIdQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var mockClientProxy = new Mock<IClientProxy>();
        var mockClients = new Mock<IHubClients>();
        mockClients.Setup(c => c.Group($"board:{trackTypeId}")).Returns(mockClientProxy.Object);
        _boardHub.Setup(h => h.Clients).Returns(mockClients.Object);

        var command = new MoveJobStageCommand(1, toStageId);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        mockClientProxy.Verify(p => p.SendCoreAsync(
            "jobMoved",
            It.Is<object?[]>(args => args.Length == 1 && args[0] is BoardJobMovedEvent),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
