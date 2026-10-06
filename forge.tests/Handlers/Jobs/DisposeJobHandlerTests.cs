using Bogus;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Moq;

using Forge.Api.Hubs;

using Forge.Api.Features.Jobs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Jobs;

public class DisposeJobHandlerTests
{
    private readonly Mock<IJobRepository> _jobRepo = new();
    private readonly Mock<IAssetRepository> _assetRepo = new();
    private readonly Mock<IMediator> _mediator = new();
    private readonly Mock<IHubContext<BoardHub>> _boardHub = new();
    private readonly Mock<IClock> _clock = new();
    private readonly AppDbContext _dbContext;
    private readonly DisposeJobHandler _handler;

    private readonly Faker _faker = new();

    public DisposeJobHandlerTests()
    {
        _dbContext = TestDbContextFactory.Create();

        var mockClients = new Mock<IHubClients>();
        var mockClientProxy = new Mock<IClientProxy>();
        mockClients.Setup(c => c.Group(It.IsAny<string>())).Returns(mockClientProxy.Object);
        _boardHub.Setup(h => h.Clients).Returns(mockClients.Object);
        _clock.Setup(c => c.UtcNow).Returns(new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero));

        _handler = new DisposeJobHandler(
            _jobRepo.Object,
            _assetRepo.Object,
            _mediator.Object,
            _boardHub.Object,
            _clock.Object,
            _dbContext);
    }

    [Fact]
    public async Task Handle_ValidDisposition_SetsDispositionFieldsAndReturnsDetail()
    {
        // Arrange
        var jobId = _faker.Random.Int(1, 100);
        var jobNumber = $"JOB-{_faker.Random.Int(1000, 9999)}";

        var job = new Job
        {
            Id = jobId,
            JobNumber = jobNumber,
            Title = _faker.Commerce.ProductName(),
            TrackTypeId = 1,
            CurrentStageId = 1,
            Disposition = null,
        };

        _jobRepo.Setup(r => r.FindAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        var expectedResult = BuildJobDetailResponse(jobId, jobNumber);
        _mediator.Setup(m => m.Send(It.IsAny<GetJobByIdQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var data = new DisposeJobRequestModel(JobDisposition.ShipToCustomer, "Ship with order");
        var command = new DisposeJobCommand(jobId, data);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        job.Disposition.Should().Be(JobDisposition.ShipToCustomer);
        job.DispositionNotes.Should().Be("Ship with order");
        job.DispositionAt.Should().NotBeNull();
        job.DispositionAt!.Value.Should().Be(_clock.Object.UtcNow);

        _jobRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private Job ArrangeJob(int jobId, string jobNumber)
    {
        var job = new Job
        {
            Id = jobId,
            JobNumber = jobNumber,
            Title = _faker.Commerce.ProductName(),
            TrackTypeId = 7,
            CurrentStageId = 1,
            Disposition = null,
        };
        _jobRepo.Setup(r => r.FindAsync(jobId, It.IsAny<CancellationToken>())).ReturnsAsync(job);
        _mediator.Setup(m => m.Send(It.IsAny<GetJobByIdQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildJobDetailResponse(jobId, jobNumber));
        return job;
    }

    [Fact]
    public async Task Handle_EnteredInError_ArchivesSoTheCardLeavesTheBoard()
    {
        var job = ArrangeJob(42, "JOB-0042");

        await _handler.Handle(
            new DisposeJobCommand(42, new DisposeJobRequestModel(JobDisposition.EnteredInError, "wrong part")),
            CancellationToken.None);

        job.Disposition.Should().Be(JobDisposition.EnteredInError);
        job.IsArchived.Should().BeTrue(
            "the board filters on IsArchived, so a mistaken entry that stays unarchived keeps its card");
    }

    [Fact]
    public async Task Handle_RealDisposition_DoesNotArchive()
    {
        var job = ArrangeJob(43, "JOB-0043");

        await _handler.Handle(
            new DisposeJobCommand(43, new DisposeJobRequestModel(JobDisposition.ShipToCustomer, null)),
            CancellationToken.None);

        job.IsArchived.Should().BeFalse("shipping is an outcome, not a retraction");
    }

    [Fact]
    public async Task Handle_WritesAnActivityLogRow()
    {
        var job = ArrangeJob(44, "JOB-0044");

        await _handler.Handle(
            new DisposeJobCommand(44, new DisposeJobRequestModel(JobDisposition.Scrap, null)),
            CancellationToken.None);

        job.ActivityLogs.Should().ContainSingle()
            .Which.Action.Should().Be(ActivityAction.StatusChanged);
    }

    [Fact]
    public async Task Handle_AlreadyDisposed_ThrowsInvalidOperationException()
    {
        // Arrange
        var jobId = _faker.Random.Int(1, 100);
        var jobNumber = $"JOB-{_faker.Random.Int(1000, 9999)}";

        var job = new Job
        {
            Id = jobId,
            JobNumber = jobNumber,
            Title = "Already Disposed",
            TrackTypeId = 1,
            CurrentStageId = 1,
            Disposition = JobDisposition.Scrap,
        };

        _jobRepo.Setup(r => r.FindAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        var command = new DisposeJobCommand(jobId, new DisposeJobRequestModel(JobDisposition.AddToInventory, null));

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{jobNumber}*already been disposed*");
    }

    [Fact]
    public async Task Handle_JobNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        var jobId = 999;

        _jobRepo.Setup(r => r.FindAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Job?)null);

        var command = new DisposeJobCommand(jobId, new DisposeJobRequestModel(JobDisposition.Scrap, null));

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage($"*{jobId}*not found*");
    }

    [Fact]
    public async Task Handle_CapitalizeAsAsset_CreatesAssetLinkedToJob()
    {
        // Arrange
        var jobId = _faker.Random.Int(1, 100);
        var jobNumber = $"JOB-{_faker.Random.Int(1000, 9999)}";
        var jobTitle = _faker.Commerce.ProductName();

        var job = new Job
        {
            Id = jobId,
            JobNumber = jobNumber,
            Title = jobTitle,
            TrackTypeId = 1,
            CurrentStageId = 1,
            Disposition = null,
        };

        _jobRepo.Setup(r => r.FindAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        var expectedResult = BuildJobDetailResponse(jobId, jobNumber);
        _mediator.Setup(m => m.Send(It.IsAny<GetJobByIdQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var data = new DisposeJobRequestModel(JobDisposition.CapitalizeAsAsset, "New tooling asset");
        var command = new DisposeJobCommand(jobId, data);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _assetRepo.Verify(r => r.AddAsync(It.Is<Asset>(a =>
            a.Name == jobTitle &&
            a.AssetType == AssetType.Tooling &&
            a.Status == AssetStatus.Active &&
            a.SourceJobId == jobId &&
            a.Notes!.Contains(jobNumber)
        ), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_CapitalizeAsAsset_SetsSourcePartIdWhenJobHasPart()
    {
        // Arrange
        var jobId = 10;
        var partId = 42;
        var jobNumber = "JOB-0042";

        var job = new Job
        {
            Id = jobId,
            JobNumber = jobNumber,
            Title = "Tooling Job",
            TrackTypeId = 1,
            CurrentStageId = 1,
            Disposition = null,
        };

        // Seed a JobPart entry so the handler can find it
        _dbContext.Set<JobPart>().Add(new JobPart { JobId = jobId, PartId = partId, Quantity = 1 });
        await _dbContext.SaveChangesAsync();

        _jobRepo.Setup(r => r.FindAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        _mediator.Setup(m => m.Send(It.IsAny<GetJobByIdQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildJobDetailResponse(jobId, jobNumber));

        var command = new DisposeJobCommand(jobId, new DisposeJobRequestModel(JobDisposition.CapitalizeAsAsset, null));

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _assetRepo.Verify(r => r.AddAsync(It.Is<Asset>(a =>
            a.SourcePartId == partId
        ), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_CapitalizeAsAsset_DoesNotCallJobRepoSaveChanges()
    {
        // Arrange
        var jobId = 5;

        var job = new Job
        {
            Id = jobId,
            JobNumber = "JOB-0005",
            Title = "Asset Job",
            TrackTypeId = 1,
            CurrentStageId = 1,
            Disposition = null,
        };

        _jobRepo.Setup(r => r.FindAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        _mediator.Setup(m => m.Send(It.IsAny<GetJobByIdQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildJobDetailResponse(jobId, "JOB-0005"));

        var command = new DisposeJobCommand(jobId, new DisposeJobRequestModel(JobDisposition.CapitalizeAsAsset, null));

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert — asset path goes through assetRepo, not jobRepo.SaveChanges
        _jobRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NotesAreTrimmed()
    {
        // Arrange
        var jobId = 7;
        var job = new Job { Id = jobId, JobNumber = "JOB-0007", Title = "T", TrackTypeId = 1, CurrentStageId = 1 };

        _jobRepo.Setup(r => r.FindAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        _mediator.Setup(m => m.Send(It.IsAny<GetJobByIdQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildJobDetailResponse(jobId, "JOB-0007"));

        var command = new DisposeJobCommand(jobId,
            new DisposeJobRequestModel(JobDisposition.HoldForReview, "  needs review  "));

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        job.DispositionNotes.Should().Be("needs review");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static JobDetailResponseModel BuildJobDetailResponse(int jobId, string jobNumber) =>
        new(jobId, jobNumber, "Title", null, 1, "Production",
            1, "Stage", "#94a3b8", null, null, null, null,
            "Normal", null, null, null, null, null, false, 1, 0, null,
            null, null, null, null, null, null, null, null, null, null, 0,
            DateTime.UtcNow, DateTime.UtcNow);
}
