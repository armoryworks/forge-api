using Bogus;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Moq;
using QBEngineer.Api.Features.Jobs;
using QBEngineer.Api.Hubs;
using QBEngineer.Core.Entities;
using QBEngineer.Core.Enums;
using QBEngineer.Core.Interfaces;
using QBEngineer.Core.Models;
using QBEngineer.Data.Context;
using QBEngineer.Tests.Helpers;

namespace QBEngineer.Tests.Handlers.Jobs;

public class CreateJobHandlerTests
{
    private readonly Mock<IJobRepository> _jobRepo = new();
    private readonly Mock<ITrackTypeRepository> _trackRepo = new();
    private readonly Mock<IMediator> _mediator = new();
    private readonly Mock<IHubContext<BoardHub>> _boardHub = new();
    private readonly CreateJobHandler _handler;
    private readonly AppDbContext _db;

    private readonly Faker _faker = new();

    public CreateJobHandlerTests()
    {
        // Setup default hub mock chain
        var mockClients = new Mock<IHubClients>();
        var mockClientProxy = new Mock<IClientProxy>();
        mockClients.Setup(c => c.Group(It.IsAny<string>())).Returns(mockClientProxy.Object);
        _boardHub.Setup(h => h.Clients).Returns(mockClients.Object);

        _db = TestDbContextFactory.Create();

        _handler = new CreateJobHandler(
            _jobRepo.Object, _trackRepo.Object, _mediator.Object, _boardHub.Object,
            Mock.Of<IBarcodeService>(),
            Mock.Of<Microsoft.AspNetCore.Http.IHttpContextAccessor>(),
            _db,
            new NoOpCloudFolderAutoCreator());
    }

    /// <summary>Test-only no-op folder auto-creator.</summary>
    private sealed class NoOpCloudFolderAutoCreator : ICloudFolderAutoCreator
    {
        public Task<EntityCloudLink?> AutoCreateAsync(
            string entityType, int entityId,
            IReadOnlyDictionary<string, string> tokenContext,
            CancellationToken ct) => Task.FromResult<EntityCloudLink?>(null);
    }

    [Fact]
    public async Task Handle_ValidCommand_CreatesJobAndReturnsDetail()
    {
        // Arrange
        var trackTypeId = _faker.Random.Int(1, 100);
        var stageId = _faker.Random.Int(1, 100);
        var stageName = _faker.Commerce.Department();
        var jobNumber = $"JOB-{_faker.Random.Int(1000, 9999)}";
        var title = _faker.Commerce.ProductName();
        var description = _faker.Lorem.Sentence();
        var customerId = _faker.Random.Int(1, 50);
        var assigneeId = _faker.Random.Int(1, 20);
        var dueDate = DateTime.UtcNow.AddDays(14);

        // Seed a compliant employee profile so AssigneeComplianceCheck passes
        _db.EmployeeProfiles.Add(new EmployeeProfile
        {
            UserId = assigneeId,
            W4CompletedAt = DateTime.UtcNow,
            I9CompletedAt = DateTime.UtcNow,
            StateWithholdingCompletedAt = DateTime.UtcNow,
            EmergencyContactName = "Jane Doe",
            EmergencyContactPhone = "(555) 123-4567",
        });
        await _db.SaveChangesAsync();

        var firstStage = new JobStage { Id = stageId, TrackTypeId = trackTypeId, Name = stageName };

        _trackRepo.Setup(r => r.FindFirstActiveStageAsync(trackTypeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(firstStage);
        _jobRepo.Setup(r => r.GenerateNextJobNumberAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(jobNumber);
        _jobRepo.Setup(r => r.GetMaxBoardPositionAsync(stageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);

        var expectedResult = new JobDetailResponseModel(
            1, jobNumber, title, description, trackTypeId, "Production",
            stageId, stageName, "#94a3b8", assigneeId, "JD", "John Doe", "#3b82f6",
            "Normal", customerId, "Acme Corp", dueDate, null, null, false, 4, 0, null,
            null, null, null, null, null, null, null, null, null, null, 0,
            DateTime.UtcNow, DateTime.UtcNow);

        _mediator.Setup(m => m.Send(It.IsAny<GetJobByIdQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var command = new CreateJobCommand(title, description, trackTypeId, assigneeId, customerId, JobPriority.Normal, dueDate);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.JobNumber.Should().Be(jobNumber);
        result.Title.Should().Be(title);

        _jobRepo.Verify(r => r.AddAsync(It.Is<Job>(j =>
            j.JobNumber == jobNumber &&
            j.Title == title &&
            j.Description == description &&
            j.TrackTypeId == trackTypeId &&
            j.CurrentStageId == stageId &&
            j.AssigneeId == assigneeId &&
            j.CustomerId == customerId &&
            j.Priority == JobPriority.Normal &&
            j.BoardPosition == 4
        ), It.IsAny<CancellationToken>()), Times.Once);

        _jobRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NoActiveStages_ThrowsKeyNotFoundException()
    {
        // Arrange
        var trackTypeId = _faker.Random.Int(1, 100);

        _trackRepo.Setup(r => r.FindFirstActiveStageAsync(trackTypeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((JobStage?)null);

        var command = new CreateJobCommand("Test Job", null, trackTypeId, null, null, null, null);

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage($"*TrackType {trackTypeId}*");
    }

    [Fact]
    public async Task Handle_NoPriority_DefaultsToNormal()
    {
        // Arrange
        var stageId = _faker.Random.Int(1, 100);
        var firstStage = new JobStage { Id = stageId, TrackTypeId = 1, Name = "Quote" };

        _trackRepo.Setup(r => r.FindFirstActiveStageAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(firstStage);
        _jobRepo.Setup(r => r.GenerateNextJobNumberAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("JOB-0001");
        _jobRepo.Setup(r => r.GetMaxBoardPositionAsync(stageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var expectedResult = new JobDetailResponseModel(
            1, "JOB-0001", "Test", null, 1, "Production",
            stageId, "Quote", "#94a3b8", null, null, null, null,
            "Normal", null, null, null, null, null, false, 1, 0, null,
            null, null, null, null, null, null, null, null, null, null, 0,
            DateTime.UtcNow, DateTime.UtcNow);

        _mediator.Setup(m => m.Send(It.IsAny<GetJobByIdQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var command = new CreateJobCommand("Test", null, 1, null, null, null, null);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _jobRepo.Verify(r => r.AddAsync(It.Is<Job>(j =>
            j.Priority == JobPriority.Normal
        ), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_CreatesActivityLog()
    {
        // Arrange
        var stageId = 5;
        var firstStage = new JobStage { Id = stageId, TrackTypeId = 1, Name = "Quote" };

        _trackRepo.Setup(r => r.FindFirstActiveStageAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(firstStage);
        _jobRepo.Setup(r => r.GenerateNextJobNumberAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("JOB-0042");
        _jobRepo.Setup(r => r.GetMaxBoardPositionAsync(stageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var expectedResult = new JobDetailResponseModel(
            1, "JOB-0042", "Test", null, 1, "Production",
            stageId, "Quote", "#94a3b8", null, null, null, null,
            "Normal", null, null, null, null, null, false, 1, 0, null,
            null, null, null, null, null, null, null, null, null, null, 0,
            DateTime.UtcNow, DateTime.UtcNow);

        _mediator.Setup(m => m.Send(It.IsAny<GetJobByIdQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var command = new CreateJobCommand("Test", null, 1, null, null, null, null);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _jobRepo.Verify(r => r.AddAsync(It.Is<Job>(j =>
            j.ActivityLogs.Count == 1 &&
            j.ActivityLogs.First().Action == ActivityAction.Created &&
            j.ActivityLogs.First().Description!.Contains("JOB-0042")
        ), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_BroadcastsSignalREvent()
    {
        // Arrange
        var trackTypeId = 2;
        var stageId = 10;
        var firstStage = new JobStage { Id = stageId, TrackTypeId = trackTypeId, Name = "Quote" };

        _trackRepo.Setup(r => r.FindFirstActiveStageAsync(trackTypeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(firstStage);
        _jobRepo.Setup(r => r.GenerateNextJobNumberAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("JOB-0001");
        _jobRepo.Setup(r => r.GetMaxBoardPositionAsync(stageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var expectedResult = new JobDetailResponseModel(
            1, "JOB-0001", "Test", null, trackTypeId, "R&D",
            stageId, "Quote", "#94a3b8", null, null, null, null,
            "Normal", null, null, null, null, null, false, 1, 0, null,
            null, null, null, null, null, null, null, null, null, null, 0,
            DateTime.UtcNow, DateTime.UtcNow);

        _mediator.Setup(m => m.Send(It.IsAny<GetJobByIdQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var mockClientProxy = new Mock<IClientProxy>();
        var mockClients = new Mock<IHubClients>();
        mockClients.Setup(c => c.Group($"board:{trackTypeId}")).Returns(mockClientProxy.Object);
        _boardHub.Setup(h => h.Clients).Returns(mockClients.Object);

        var command = new CreateJobCommand("Test", null, trackTypeId, null, null, null, null);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        mockClientProxy.Verify(p => p.SendCoreAsync(
            "jobCreated",
            It.Is<object?[]>(args => args.Length == 1 && args[0] is BoardJobCreatedEvent),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
