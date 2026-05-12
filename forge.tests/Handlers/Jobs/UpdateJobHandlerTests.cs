using Bogus;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Moq;
using Forge.Api.Features.Jobs;
using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Jobs;

public class UpdateJobHandlerTests
{
    private readonly Mock<IJobRepository> _jobRepo = new();
    private readonly Mock<IMediator> _mediator = new();
    private readonly Mock<IHubContext<BoardHub>> _boardHub = new();
    private readonly UpdateJobHandler _handler;

    private readonly Faker _faker = new();

    public UpdateJobHandlerTests()
    {
        var mockClients = new Mock<IHubClients>();
        var mockClientProxy = new Mock<IClientProxy>();
        mockClients.Setup(c => c.Group(It.IsAny<string>())).Returns(mockClientProxy.Object);
        _boardHub.Setup(h => h.Clients).Returns(mockClients.Object);

        _handler = new UpdateJobHandler(
            _jobRepo.Object,
            Mock.Of<IActivityLogRepository>(),
            _mediator.Object,
            _boardHub.Object,
            Mock.Of<Microsoft.AspNetCore.Http.IHttpContextAccessor>(),
            TestDbContextFactory.Create());
    }

    private Job CreateExistingJob(int id = 1)
    {
        return new Job
        {
            Id = id,
            JobNumber = $"JOB-{_faker.Random.Int(1000, 9999)}",
            Title = _faker.Commerce.ProductName(),
            Description = _faker.Lorem.Sentence(),
            TrackTypeId = _faker.Random.Int(1, 5),
            CurrentStageId = _faker.Random.Int(1, 20),
            Priority = JobPriority.Normal,
            BoardPosition = 1,
        };
    }

    private JobDetailResponseModel CreateExpectedResult(Job job)
    {
        return new JobDetailResponseModel(
            job.Id, job.JobNumber, job.Title, job.Description,
            job.TrackTypeId, "Production", job.CurrentStageId, "In Production", "#94a3b8",
            job.AssigneeId, null, null, null, job.Priority.ToString(),
            job.CustomerId, null, job.DueDate, null, null, false,
            job.BoardPosition, job.IterationCount, job.IterationNotes,
            null, null, null, null, null, null, null, null, null, null, 0,
            DateTime.UtcNow, DateTime.UtcNow);
    }

    [Fact]
    public async Task Handle_ValidCommand_UpdatesTitleAndDescription()
    {
        // Arrange
        var job = CreateExistingJob();
        var newTitle = _faker.Commerce.ProductName();
        var newDescription = _faker.Lorem.Paragraph();

        _jobRepo.Setup(r => r.FindAsync(job.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        var expectedResult = CreateExpectedResult(job);
        _mediator.Setup(m => m.Send(It.IsAny<GetJobByIdQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var command = new UpdateJobCommand(job.Id, newTitle, newDescription, null, null, null, null, null, null);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        job.Title.Should().Be(newTitle);
        job.Description.Should().Be(newDescription);
        _jobRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_JobNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        var jobId = _faker.Random.Int(1, 1000);
        _jobRepo.Setup(r => r.FindAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Job?)null);

        var command = new UpdateJobCommand(jobId, "Updated Title", null, null, null, null, null, null, null);

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage($"*{jobId}*");
    }

    [Fact]
    public async Task Handle_NullFields_DoesNotOverwrite()
    {
        // Arrange
        var job = CreateExistingJob();
        var originalTitle = job.Title;
        var originalDescription = job.Description;
        var originalPriority = job.Priority;

        _jobRepo.Setup(r => r.FindAsync(job.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        var expectedResult = CreateExpectedResult(job);
        _mediator.Setup(m => m.Send(It.IsAny<GetJobByIdQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        // All null fields — nothing should change
        var command = new UpdateJobCommand(job.Id, null, null, null, null, null, null, null, null);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        job.Title.Should().Be(originalTitle);
        job.Description.Should().Be(originalDescription);
        job.Priority.Should().Be(originalPriority);
    }

    [Fact]
    public async Task Handle_UpdatesPriority_WhenProvided()
    {
        // Arrange
        var job = CreateExistingJob();
        job.Priority = JobPriority.Normal;

        _jobRepo.Setup(r => r.FindAsync(job.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        var expectedResult = CreateExpectedResult(job);
        _mediator.Setup(m => m.Send(It.IsAny<GetJobByIdQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var command = new UpdateJobCommand(job.Id, null, null, null, null, JobPriority.Urgent, null, null, null);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        job.Priority.Should().Be(JobPriority.Urgent);
    }

    [Fact]
    public async Task Handle_UpdatesIterationFields()
    {
        // Arrange
        var job = CreateExistingJob();

        _jobRepo.Setup(r => r.FindAsync(job.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        var expectedResult = CreateExpectedResult(job);
        _mediator.Setup(m => m.Send(It.IsAny<GetJobByIdQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var command = new UpdateJobCommand(job.Id, null, null, null, null, null, null, 3, "Third iteration with new tooling");

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        job.IterationCount.Should().Be(3);
        job.IterationNotes.Should().Be("Third iteration with new tooling");
    }

    [Fact]
    public async Task Handle_BroadcastsToBoard_AndJobGroup()
    {
        // Arrange
        var job = CreateExistingJob();
        var boardProxy = new Mock<IClientProxy>();
        var jobProxy = new Mock<IClientProxy>();
        var mockClients = new Mock<IHubClients>();

        mockClients.Setup(c => c.Group($"board:{job.TrackTypeId}")).Returns(boardProxy.Object);
        mockClients.Setup(c => c.Group($"job:{job.Id}")).Returns(jobProxy.Object);
        _boardHub.Setup(h => h.Clients).Returns(mockClients.Object);

        _jobRepo.Setup(r => r.FindAsync(job.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        var expectedResult = CreateExpectedResult(job);
        _mediator.Setup(m => m.Send(It.IsAny<GetJobByIdQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var command = new UpdateJobCommand(job.Id, "New Title", null, null, null, null, null, null, null);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert — both groups receive the event
        boardProxy.Verify(p => p.SendCoreAsync("jobUpdated", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Once);
        jobProxy.Verify(p => p.SendCoreAsync("jobUpdated", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
