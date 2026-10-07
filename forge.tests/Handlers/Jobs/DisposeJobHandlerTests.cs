using Bogus;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Hubs;

using Forge.Api.Features.Jobs;
using Forge.Api.Features.Jobs.ProductionRuns;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Integrations;
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
        var command = new DisposeJobCommand(jobId, data, 1);

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
            new DisposeJobCommand(42, new DisposeJobRequestModel(JobDisposition.EnteredInError, "wrong part"), 1),
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
            new DisposeJobCommand(43, new DisposeJobRequestModel(JobDisposition.ShipToCustomer, null), 1),
            CancellationToken.None);

        job.IsArchived.Should().BeFalse("shipping is an outcome, not a retraction");
    }

    [Fact]
    public async Task Handle_WritesAnActivityLogRow()
    {
        var job = ArrangeJob(44, "JOB-0044");

        await _handler.Handle(
            new DisposeJobCommand(44, new DisposeJobRequestModel(JobDisposition.Scrap, null), 1),
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

        var command = new DisposeJobCommand(jobId, new DisposeJobRequestModel(JobDisposition.AddToInventory, null), 1);

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

        var command = new DisposeJobCommand(jobId, new DisposeJobRequestModel(JobDisposition.Scrap, null), 1);

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
        var command = new DisposeJobCommand(jobId, data, 1);

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

        var command = new DisposeJobCommand(jobId, new DisposeJobRequestModel(JobDisposition.CapitalizeAsAsset, null), 1);

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

        var command = new DisposeJobCommand(jobId, new DisposeJobRequestModel(JobDisposition.CapitalizeAsAsset, null), 1);

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
            new DisposeJobRequestModel(JobDisposition.HoldForReview, "  needs review  "), 1);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        job.DispositionNotes.Should().Be("needs review");
    }

    [Fact]
    public async Task Handle_HeldJob_CanBeReleasedAndScrapped()
    {
        var job = ArrangeJob(50, "JOB-0050");
        job.Disposition = JobDisposition.HoldForReview;
        job.DispositionNotes = "check the bore";

        await _handler.Handle(
            new DisposeJobCommand(50, new DisposeJobRequestModel(JobDisposition.Scrap, "bore out of tolerance"), 1),
            CancellationToken.None);

        job.Disposition.Should().Be(JobDisposition.Scrap);
        job.DispositionNotes.Should().Be("bore out of tolerance");
        job.ActivityLogs.Should().ContainSingle()
            .Which.Description.Should().Be("Released from hold (was: check the bore) and disposed as Scrap.");
        _jobRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_HeldJob_CannotBeHeldAgain()
    {
        var job = ArrangeJob(51, "JOB-0051");
        job.Disposition = JobDisposition.HoldForReview;
        job.DispositionNotes = "check the bore";

        var act = () => _handler.Handle(
            new DisposeJobCommand(51, new DisposeJobRequestModel(JobDisposition.HoldForReview, "again"), 1),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already on hold*");
        job.DispositionNotes.Should().Be("check the bore");
    }

    [Fact]
    public async Task Handle_EnteredInError_RefusedWhenLotsExist()
    {
        var job = ArrangeJob(52, "JOB-0052");
        _dbContext.LotRecords.Add(new LotRecord { LotNumber = "LOT-1", PartId = 9, JobId = 52, Quantity = 10 });
        await _dbContext.SaveChangesAsync();

        var act = () => _handler.Handle(
            new DisposeJobCommand(52, new DisposeJobRequestModel(JobDisposition.EnteredInError, "duplicate"), 1),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage(DisposeJobHandler.ProductionHistoryMessage);
        job.Disposition.Should().BeNull();
        job.IsArchived.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_EnteredInError_RefusedWhenARunRecordedOutput()
    {
        ArrangeJob(53, "JOB-0053");
        _dbContext.ProductionRuns.Add(new ProductionRun
        {
            JobId = 53, PartId = 9, RunNumber = "RUN-53", TargetQuantity = 10, ScrapQuantity = 2,
        });
        await _dbContext.SaveChangesAsync();

        var act = () => _handler.Handle(
            new DisposeJobCommand(53, new DisposeJobRequestModel(JobDisposition.EnteredInError, "duplicate"), 1),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage(DisposeJobHandler.ProductionHistoryMessage);
    }

    [Fact]
    public async Task Handle_EnteredInError_AllowedWhenIssuedMaterialWasReturned()
    {
        var job = ArrangeJob(54, "JOB-0054");
        _dbContext.MaterialIssues.AddRange(
            new MaterialIssue { JobId = 54, PartId = 9, Quantity = 5, IssueType = MaterialIssueType.Issue },
            new MaterialIssue { JobId = 54, PartId = 9, Quantity = 5, IssueType = MaterialIssueType.Return });
        await _dbContext.SaveChangesAsync();

        await _handler.Handle(
            new DisposeJobCommand(54, new DisposeJobRequestModel(JobDisposition.EnteredInError, "duplicate"), 1),
            CancellationToken.None);

        job.IsArchived.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_AddToInventory_WithoutPart_IsRefused()
    {
        var job = ArrangeJob(55, "JOB-0055");

        var act = () => _handler.Handle(
            new DisposeJobCommand(55, new DisposeJobRequestModel(JobDisposition.AddToInventory, null, 5, 1), 1),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage(DisposeJobHandler.NoPartMessage);
        job.Disposition.Should().BeNull();
    }

    [Fact]
    public async Task Handle_AddToInventory_StocksTheGoodQuantityIntoTheChosenBin()
    {
        var defaultBin = new StorageLocation { Name = "B-01", LocationType = LocationType.Bin, IsActive = true };
        var chosenBin = new StorageLocation { Name = "A-03", LocationType = LocationType.Bin, IsActive = true };
        _dbContext.StorageLocations.AddRange(defaultBin, chosenBin);
        await _dbContext.SaveChangesAsync();

        var part = new Part
        {
            PartNumber = "40-1700M", Name = "Housing", InventoryClass = InventoryClass.FinishedGood,
            DefaultBinId = defaultBin.Id,
        };
        _dbContext.Parts.Add(part);
        await _dbContext.SaveChangesAsync();
        var job = new Job { JobNumber = "JOB-0056", Title = "Make housings", TrackTypeId = 1, CurrentStageId = 1, PartId = part.Id };
        _dbContext.Jobs.Add(job);
        await _dbContext.SaveChangesAsync();

        _jobRepo.Setup(r => r.FindAsync(job.Id, It.IsAny<CancellationToken>())).ReturnsAsync(job);
        _mediator.Setup(m => m.Send(It.IsAny<GetJobByIdQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildJobDetailResponse(job.Id, job.JobNumber));
        var receive = new ReceiveProductionRunToStockHandler(
            _dbContext, new SystemClock(), new InventoryRepository(_dbContext), posting: null);
        _mediator.Setup(m => m.Send(It.IsAny<ReceiveProductionRunToStockCommand>(), It.IsAny<CancellationToken>()))
            .Returns((IRequest<ProductionRunResponseModel> c, CancellationToken ct) =>
                receive.Handle((ReceiveProductionRunToStockCommand)c, ct));

        await _handler.Handle(
            new DisposeJobCommand(job.Id,
                new DisposeJobRequestModel(JobDisposition.AddToInventory, null, 500m, chosenBin.Id), 1),
            CancellationToken.None);

        _dbContext.ChangeTracker.Clear();
        var stocked = await _dbContext.BinContents
            .SingleAsync(b => b.EntityType == "part" && b.EntityId == part.Id);
        stocked.LocationId.Should().Be(chosenBin.Id);
        stocked.Quantity.Should().Be(500m);

        var movement = await _dbContext.BinMovements
            .SingleAsync(m => m.EntityType == "part" && m.EntityId == part.Id);
        movement.ToLocationId.Should().Be(chosenBin.Id);
        movement.Quantity.Should().Be(500m);
        movement.Reason.Should().Be(BinMovementReason.Receive);

        var run = await _dbContext.ProductionRuns.SingleAsync(r => r.JobId == job.Id);
        run.Status.Should().Be(ProductionRunStatus.Completed);
        run.CompletedQuantity.Should().Be(500);
        run.ReceivedQuantity.Should().Be(500);
        run.ReceivedToStockAt.Should().NotBeNull();

        var disposed = await _dbContext.Jobs.SingleAsync(j => j.Id == job.Id);
        disposed.Disposition.Should().Be(JobDisposition.AddToInventory);
    }

    [Fact]
    public async Task Handle_AddToInventory_ReusesAnUnreceivedCompletedRun()
    {
        var bin = new StorageLocation { Name = "A-03", LocationType = LocationType.Bin, IsActive = true };
        _dbContext.StorageLocations.Add(bin);
        var part = new Part { PartNumber = "P-57", Name = "Bracket", InventoryClass = InventoryClass.FinishedGood };
        _dbContext.Parts.Add(part);
        await _dbContext.SaveChangesAsync();
        var job = new Job { JobNumber = "JOB-0057", Title = "Brackets", TrackTypeId = 1, CurrentStageId = 1, PartId = part.Id };
        _dbContext.Jobs.Add(job);
        await _dbContext.SaveChangesAsync();
        _dbContext.ProductionRuns.Add(new ProductionRun
        {
            JobId = job.Id, PartId = part.Id, RunNumber = "RUN-57", TargetQuantity = 100,
            CompletedQuantity = 90, Status = ProductionRunStatus.Completed,
        });
        await _dbContext.SaveChangesAsync();

        _jobRepo.Setup(r => r.FindAsync(job.Id, It.IsAny<CancellationToken>())).ReturnsAsync(job);
        _mediator.Setup(m => m.Send(It.IsAny<GetJobByIdQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildJobDetailResponse(job.Id, job.JobNumber));
        var receive = new ReceiveProductionRunToStockHandler(
            _dbContext, new SystemClock(), new InventoryRepository(_dbContext), posting: null);
        _mediator.Setup(m => m.Send(It.IsAny<ReceiveProductionRunToStockCommand>(), It.IsAny<CancellationToken>()))
            .Returns((IRequest<ProductionRunResponseModel> c, CancellationToken ct) =>
                receive.Handle((ReceiveProductionRunToStockCommand)c, ct));

        await _handler.Handle(
            new DisposeJobCommand(job.Id,
                new DisposeJobRequestModel(JobDisposition.AddToInventory, null, 95m, bin.Id), 1),
            CancellationToken.None);

        _dbContext.ChangeTracker.Clear();
        var run = await _dbContext.ProductionRuns.SingleAsync(r => r.JobId == job.Id);
        run.RunNumber.Should().Be("RUN-57");
        run.ReceivedQuantity.Should().Be(95);
    }

    [Theory]
    [InlineData(JobDisposition.Scrap)]
    [InlineData(JobDisposition.HoldForReview)]
    [InlineData(JobDisposition.EnteredInError)]
    public void Validator_RequiresAReason(JobDisposition disposition)
    {
        var result = new DisposeJobCommandValidator()
            .Validate(new DisposeJobCommand(1, new DisposeJobRequestModel(disposition, "   "), 1));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage == "A reason is required for this disposition.");
    }

    [Theory]
    [InlineData(JobDisposition.ShipToCustomer)]
    [InlineData(JobDisposition.Other)]
    public void Validator_DoesNotRequireAReasonForOutcomes(JobDisposition disposition)
    {
        new DisposeJobCommandValidator()
            .Validate(new DisposeJobCommand(1, new DisposeJobRequestModel(disposition, null), 1))
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validator_RejectsAFractionalGoodQuantity()
    {
        new DisposeJobCommandValidator()
            .Validate(new DisposeJobCommand(1,
                new DisposeJobRequestModel(JobDisposition.AddToInventory, null, 2.5m, 1), 1))
            .IsValid.Should().BeFalse();
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static JobDetailResponseModel BuildJobDetailResponse(int jobId, string jobNumber) =>
        new(jobId, jobNumber, "Title", null, 1, "Production",
            1, "Stage", "#94a3b8", null, null, null, null,
            "Normal", null, null, null, null, null, false, 1, 0, null,
            null, null, null, null, null, null, null, null, null, null, 0,
            DateTime.UtcNow, DateTime.UtcNow);
}
