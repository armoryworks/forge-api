using Bogus;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Moq;

using Forge.Api.Features.Jobs;
using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Jobs;

public class ExplodeJobBomHandlerTests
{
    private readonly Mock<IJobRepository> _jobRepo = new();
    private readonly Mock<IBarcodeService> _barcodes = new();
    private readonly Mock<IHubContext<BoardHub>> _boardHub = new();
    private readonly Mock<IClientProxy> _boardGroup = new();
    private readonly AppDbContext _dbContext;
    private readonly ExplodeJobBomHandler _handler;

    private readonly Faker _faker = new();

    public ExplodeJobBomHandlerTests()
    {
        _dbContext = TestDbContextFactory.Create();
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(_boardGroup.Object);
        _boardHub.SetupGet(h => h.Clients).Returns(clients.Object);
        _handler = new ExplodeJobBomHandler(_dbContext, _jobRepo.Object, _barcodes.Object, _boardHub.Object);
    }

    [Fact]
    public async Task Handle_MakeBomLine_CreatesChildJobAndJobLinks()
    {
        // Arrange
        var (trackType, stage, parentPart, parentJob) = await SeedBaseEntitiesAsync();

        var childPart = new Part { PartNumber = "CHILD-001", Description = "Child Widget" };
        _dbContext.Parts.Add(childPart);
        await _dbContext.SaveChangesAsync();

        _dbContext.BOMLines.Add(new BOMLine
        {
            ParentPartId = parentPart.Id,
            ChildPartId = childPart.Id,
            Quantity = 2,
            SourceType = BOMSourceType.Make,
            SortOrder = 1,
        });
        await _dbContext.SaveChangesAsync();

        var childJobNumber = "JOB-0002";
        _jobRepo.Setup(r => r.GenerateNextJobNumberAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(childJobNumber);
        _jobRepo.Setup(r => r.GetMaxBoardPositionAsync(stage.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var command = new ExplodeJobBomCommand(parentJob.Id);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.ParentJobId.Should().Be(parentJob.Id);
        result.CreatedJobs.Should().HaveCount(1);
        result.CreatedJobs[0].JobNumber.Should().Be(childJobNumber);
        result.CreatedJobs[0].PartId.Should().Be(childPart.Id);
        result.CreatedJobs[0].Quantity.Should().Be(2);

        _jobRepo.Verify(r => r.AddAsync(It.Is<Job>(j =>
            j.JobNumber == childJobNumber &&
            j.ParentJobId == parentJob.Id &&
            j.PartId == childPart.Id &&
            j.TrackTypeId == trackType.Id
        ), It.IsAny<CancellationToken>()), Times.Once);

        // Bidirectional links should be created
        var links = _dbContext.Set<JobLink>().ToList();
        links.Should().HaveCount(2);
        links.Should().Contain(l => l.LinkType == JobLinkType.Parent);
        links.Should().Contain(l => l.LinkType == JobLinkType.Child);

        // Each child job gets a scannable barcode and a live-board broadcast —
        // without the broadcast the explosion looked like a no-op to the user.
        _barcodes.Verify(b => b.CreateBarcodeAsync(
            BarcodeEntityType.Job, It.IsAny<int>(), childJobNumber, It.IsAny<CancellationToken>()), Times.Once);
        _boardGroup.Verify(p => p.SendCoreAsync(
            "jobCreated", It.IsAny<object[]>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_BuyBomLine_ReturnsBuyItemsWithoutCreatingJobs()
    {
        // Arrange
        var (_, _, parentPart, parentJob) = await SeedBaseEntitiesAsync();

        var buyPart = new Part { PartNumber = "BUY-001", Description = "Purchased Component" };
        _dbContext.Parts.Add(buyPart);
        await _dbContext.SaveChangesAsync();

        _dbContext.BOMLines.Add(new BOMLine
        {
            ParentPartId = parentPart.Id,
            ChildPartId = buyPart.Id,
            Quantity = 4,
            SourceType = BOMSourceType.Buy,
            LeadTimeDays = 7,
            SortOrder = 1,
        });
        await _dbContext.SaveChangesAsync();

        var command = new ExplodeJobBomCommand(parentJob.Id);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.CreatedJobs.Should().BeEmpty();
        result.BuyItems.Should().HaveCount(1);
        result.BuyItems[0].PartId.Should().Be(buyPart.Id);
        result.BuyItems[0].Quantity.Should().Be(4);
        result.BuyItems[0].LeadTimeDays.Should().Be(7);

        _jobRepo.Verify(r => r.AddAsync(It.IsAny<Job>(), It.IsAny<CancellationToken>()), Times.Never);
        _boardGroup.Verify(p => p.SendCoreAsync(
            It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_StockBomLine_AutoReservesAvailableStock()
    {
        // Arrange
        var (_, _, parentPart, parentJob) = await SeedBaseEntitiesAsync();

        var stockPart = new Part { PartNumber = "STK-001", Description = "Stock Item" };
        _dbContext.Parts.Add(stockPart);
        await _dbContext.SaveChangesAsync();

        var location = new StorageLocation { Name = "Bin 1" };
        _dbContext.StorageLocations.Add(location);
        await _dbContext.SaveChangesAsync();

        var binContent = new BinContent
        {
            LocationId = location.Id,
            EntityType = "part",
            EntityId = stockPart.Id,
            Quantity = 10,
            ReservedQuantity = 0,
            PlacedBy = 1,
            PlacedAt = DateTime.UtcNow,
        };
        _dbContext.BinContents.Add(binContent);

        _dbContext.BOMLines.Add(new BOMLine
        {
            ParentPartId = parentPart.Id,
            ChildPartId = stockPart.Id,
            Quantity = 3,
            SourceType = BOMSourceType.Stock,
            SortOrder = 1,
        });
        await _dbContext.SaveChangesAsync();

        var command = new ExplodeJobBomCommand(parentJob.Id);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.StockItems.Should().HaveCount(1);
        result.StockItems[0].Quantity.Should().Be(3);
        result.StockItems[0].ReservedQuantity.Should().Be(3);
        result.StockItems[0].HasShortfall.Should().BeFalse();

        // Verify bin content reserved quantity was updated
        binContent.ReservedQuantity.Should().Be(3);

        // Verify a reservation record was added
        var reservations = _dbContext.Set<Reservation>().ToList();
        reservations.Should().HaveCount(1);
        reservations[0].Quantity.Should().Be(3);
        reservations[0].JobId.Should().Be(parentJob.Id);
    }

    [Fact]
    public async Task Handle_StockBomLine_ShortfallWhenInsufficientStock()
    {
        // Arrange
        var (_, _, parentPart, parentJob) = await SeedBaseEntitiesAsync();

        var stockPart = new Part { PartNumber = "STK-002", Description = "Low Stock Item" };
        _dbContext.Parts.Add(stockPart);
        await _dbContext.SaveChangesAsync();

        var location = new StorageLocation { Name = "Bin 2" };
        _dbContext.StorageLocations.Add(location);
        await _dbContext.SaveChangesAsync();

        // Only 2 available, but BOM requires 5
        var binContent = new BinContent
        {
            LocationId = location.Id,
            EntityType = "part",
            EntityId = stockPart.Id,
            Quantity = 2,
            ReservedQuantity = 0,
            PlacedBy = 1,
            PlacedAt = DateTime.UtcNow,
        };
        _dbContext.BinContents.Add(binContent);

        _dbContext.BOMLines.Add(new BOMLine
        {
            ParentPartId = parentPart.Id,
            ChildPartId = stockPart.Id,
            Quantity = 5,
            SourceType = BOMSourceType.Stock,
            SortOrder = 1,
        });
        await _dbContext.SaveChangesAsync();

        var command = new ExplodeJobBomCommand(parentJob.Id);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.StockItems[0].ReservedQuantity.Should().Be(2);
        result.StockItems[0].HasShortfall.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_JobNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        var command = new ExplodeJobBomCommand(999);

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*999*not found*");
    }

    [Fact]
    public async Task Handle_JobHasNoPartId_ThrowsInvalidOperationException()
    {
        // Arrange
        var trackType = new TrackType { Name = "Production", Code = "prod", IsActive = true };
        _dbContext.TrackTypes.Add(trackType);
        await _dbContext.SaveChangesAsync();

        var stage = new JobStage { TrackTypeId = trackType.Id, Name = "Stage 1", Code = "s1", SortOrder = 1 };
        _dbContext.JobStages.Add(stage);
        await _dbContext.SaveChangesAsync();

        var job = new Job
        {
            JobNumber = "JOB-NOPART",
            Title = "No Part Job",
            TrackTypeId = trackType.Id,
            CurrentStageId = stage.Id,
            PartId = null,
        };
        _dbContext.Jobs.Add(job);
        await _dbContext.SaveChangesAsync();

        var command = new ExplodeJobBomCommand(job.Id);

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{job.Id}*no associated part*");
    }

    [Fact]
    public async Task Handle_PartHasNoBomLines_ThrowsInvalidOperationException()
    {
        // Arrange
        var (_, _, parentPart, parentJob) = await SeedBaseEntitiesAsync();
        // No BOM lines seeded for parentPart

        var command = new ExplodeJobBomCommand(parentJob.Id);

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*no BOM lines*");
    }

    [Fact]
    public async Task Handle_MixedBomLines_CorrectlyCategorizesEachType()
    {
        // Arrange
        var (_, stage, parentPart, parentJob) = await SeedBaseEntitiesAsync();

        var makePart = new Part { PartNumber = "MK-001", Description = "Make Part" };
        var buyPart = new Part { PartNumber = "BY-001", Description = "Buy Part" };
        var stockPart = new Part { PartNumber = "SK-001", Description = "Stock Part" };
        _dbContext.Parts.AddRange(makePart, buyPart, stockPart);
        await _dbContext.SaveChangesAsync();

        var location = new StorageLocation { Name = "Bin 3" };
        _dbContext.StorageLocations.Add(location);
        await _dbContext.SaveChangesAsync();

        _dbContext.BinContents.Add(new BinContent
        {
            LocationId = location.Id,
            EntityType = "part",
            EntityId = stockPart.Id,
            Quantity = 100,
            ReservedQuantity = 0,
            PlacedBy = 1,
            PlacedAt = DateTime.UtcNow,
        });

        _dbContext.BOMLines.AddRange(
            new BOMLine { ParentPartId = parentPart.Id, ChildPartId = makePart.Id, Quantity = 1, SourceType = BOMSourceType.Make, SortOrder = 1 },
            new BOMLine { ParentPartId = parentPart.Id, ChildPartId = buyPart.Id, Quantity = 2, SourceType = BOMSourceType.Buy, SortOrder = 2 },
            new BOMLine { ParentPartId = parentPart.Id, ChildPartId = stockPart.Id, Quantity = 3, SourceType = BOMSourceType.Stock, SortOrder = 3 }
        );
        await _dbContext.SaveChangesAsync();

        _jobRepo.Setup(r => r.GenerateNextJobNumberAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("JOB-AUTO");
        _jobRepo.Setup(r => r.GetMaxBoardPositionAsync(stage.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var command = new ExplodeJobBomCommand(parentJob.Id);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.CreatedJobs.Should().HaveCount(1);
        result.BuyItems.Should().HaveCount(1);
        result.StockItems.Should().HaveCount(1);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task<(TrackType trackType, JobStage stage, Part parentPart, Job parentJob)> SeedBaseEntitiesAsync()
    {
        var trackType = new TrackType { Name = "Production", Code = "prod", IsActive = true };
        _dbContext.TrackTypes.Add(trackType);
        await _dbContext.SaveChangesAsync();

        var stage = new JobStage { TrackTypeId = trackType.Id, Name = "Stage 1", Code = "s1", SortOrder = 1 };
        _dbContext.JobStages.Add(stage);
        await _dbContext.SaveChangesAsync();

        var parentPart = new Part
        {
            PartNumber = $"PARENT-{_faker.Random.AlphaNumeric(4)}",
            Description = "Parent Assembly",
        };
        _dbContext.Parts.Add(parentPart);
        await _dbContext.SaveChangesAsync();

        var parentJob = new Job
        {
            JobNumber = "JOB-0001",
            Title = "Parent Job",
            TrackTypeId = trackType.Id,
            CurrentStageId = stage.Id,
            PartId = parentPart.Id,
        };
        _dbContext.Jobs.Add(parentJob);
        await _dbContext.SaveChangesAsync();

        return (trackType, stage, parentPart, parentJob);
    }
}
