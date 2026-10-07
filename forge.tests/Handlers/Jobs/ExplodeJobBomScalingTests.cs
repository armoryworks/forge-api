using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Features.Jobs;
using Forge.Api.Hubs;
using Forge.Api.Middleware;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Jobs;

public class ExplodeJobBomScalingTests
{
    private static readonly DateTimeOffset ParentDue = new(2026, 11, 20, 0, 0, 0, TimeSpan.Zero);

    private readonly Mock<IJobRepository> _jobRepo = new();
    private readonly AppDbContext _db;
    private readonly ExplodeJobBomHandler _handler;
    private int _jobCounter = 100;

    public ExplodeJobBomScalingTests()
    {
        _db = TestDbContextFactory.Create();
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(Mock.Of<IClientProxy>());
        var hub = new Mock<IHubContext<BoardHub>>();
        hub.SetupGet(h => h.Clients).Returns(clients.Object);

        _jobRepo.Setup(r => r.GenerateNextJobNumberAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => $"J-{++_jobCounter}");
        _jobRepo.Setup(r => r.GetMaxBoardPositionAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        _handler = new ExplodeJobBomHandler(_db, _jobRepo.Object, Mock.Of<IBarcodeService>(), hub.Object);
    }

    [Fact]
    public async Task Explode_ScalesEveryLineByTheParentBuildQuantity()
    {
        var (parentPart, parentJob) = await SeedParentAsync(buildQty: 500m);
        var each = await SeedUomAsync("ea", "Each", UomCategory.Count, 0);
        var makePart = await SeedPartAsync("MK-1", each);
        var buyPart = await SeedPartAsync("BY-1", each);
        var stockPart = await SeedPartAsync("SK-1", each);

        _db.BOMLines.AddRange(
            new BOMLine { ParentPartId = parentPart.Id, ChildPartId = makePart.Id, Quantity = 2, SourceType = BOMSourceType.Make, SortOrder = 1 },
            new BOMLine { ParentPartId = parentPart.Id, ChildPartId = buyPart.Id, Quantity = 3, SourceType = BOMSourceType.Buy, LeadTimeDays = 10, SortOrder = 2 },
            new BOMLine { ParentPartId = parentPart.Id, ChildPartId = stockPart.Id, Quantity = 1, SourceType = BOMSourceType.Stock, SortOrder = 3 });
        await SeedStockAsync(stockPart, 300m);
        await _db.SaveChangesAsync();

        var result = await _handler.Handle(new ExplodeJobBomCommand(parentJob.Id), CancellationToken.None);

        result.CreatedJobs.Should().ContainSingle();
        result.CreatedJobs[0].Quantity.Should().Be(1000m);
        result.CreatedJobs[0].DueDate.Should().Be(ParentDue);

        var childJob = await _db.Jobs.SingleAsync(j => j.ParentJobId == parentJob.Id);
        childJob.Priority.Should().Be(JobPriority.Urgent);
        childJob.DueDate.Should().Be(ParentDue);
        (await _db.Set<JobPart>().SingleAsync(jp => jp.JobId == childJob.Id)).Quantity.Should().Be(1000m);

        result.BuyItems.Should().ContainSingle();
        result.BuyItems[0].Quantity.Should().Be(1500m);
        result.BuyItems[0].NeedByDate.Should().Be(ParentDue.AddDays(-10));

        result.StockItems.Should().ContainSingle();
        result.StockItems[0].Quantity.Should().Be(500m);
        result.StockItems[0].ReservedQuantity.Should().Be(300m);
        result.StockItems[0].HasShortfall.Should().BeTrue();
    }

    [Fact]
    public async Task Explode_WithoutAJobPart_BuildsOne()
    {
        var (parentPart, parentJob) = await SeedParentAsync(buildQty: null);
        var makePart = await SeedPartAsync("MK-2", uom: null);
        _db.BOMLines.Add(new BOMLine { ParentPartId = parentPart.Id, ChildPartId = makePart.Id, Quantity = 2.5m, SourceType = BOMSourceType.Make, SortOrder = 1 });
        await _db.SaveChangesAsync();

        var result = await _handler.Handle(new ExplodeJobBomCommand(parentJob.Id), CancellationToken.None);

        result.CreatedJobs.Single().Quantity.Should().Be(2.5m);
    }

    [Fact]
    public async Task Explode_RoundsUpOnlyForEachUnits()
    {
        var (parentPart, parentJob) = await SeedParentAsync(buildQty: 3m);
        var each = await SeedUomAsync("ea", "Each", UomCategory.Count, 0);
        var kg = await SeedUomAsync("kg", "Kilogram", UomCategory.Weight, 3);
        var piecePart = await SeedPartAsync("PC-1", each);
        var resinPart = await SeedPartAsync("RS-1", kg);

        _db.BOMLines.AddRange(
            new BOMLine { ParentPartId = parentPart.Id, ChildPartId = piecePart.Id, Quantity = 0.5m, SourceType = BOMSourceType.Buy, SortOrder = 1 },
            new BOMLine { ParentPartId = parentPart.Id, ChildPartId = resinPart.Id, Quantity = 0.25m, SourceType = BOMSourceType.Buy, SortOrder = 2 });
        await _db.SaveChangesAsync();

        var result = await _handler.Handle(new ExplodeJobBomCommand(parentJob.Id), CancellationToken.None);

        result.BuyItems.Single(b => b.PartId == piecePart.Id).Quantity.Should().Be(2m);
        result.BuyItems.Single(b => b.PartId == resinPart.Id).Quantity.Should().Be(0.75m);
    }

    [Fact]
    public async Task Explode_RoundsUpForAnyCountOrWholeNumberUnit()
    {
        var (parentPart, parentJob) = await SeedParentAsync(buildQty: 5m);
        var pieces = await SeedUomAsync("pcs", "Pieces", UomCategory.Count, 2);
        var roll = await SeedUomAsync("roll", "Roll", UomCategory.Length, 0);
        var metre = await SeedUomAsync("m", "Metre", UomCategory.Length, 2);
        var piecePart = await SeedPartAsync("PCS-1", pieces);
        var rollPart = await SeedPartAsync("ROLL-1", roll);
        var wirePart = await SeedPartAsync("WIRE-1", metre);

        _db.BOMLines.AddRange(
            new BOMLine { ParentPartId = parentPart.Id, ChildPartId = piecePart.Id, Quantity = 0.5m, SourceType = BOMSourceType.Buy, SortOrder = 1 },
            new BOMLine { ParentPartId = parentPart.Id, ChildPartId = rollPart.Id, Quantity = 0.1m, SourceType = BOMSourceType.Buy, SortOrder = 2 },
            new BOMLine { ParentPartId = parentPart.Id, ChildPartId = wirePart.Id, Quantity = 0.25m, SourceType = BOMSourceType.Buy, SortOrder = 3 });
        await _db.SaveChangesAsync();

        var result = await _handler.Handle(new ExplodeJobBomCommand(parentJob.Id), CancellationToken.None);

        result.BuyItems.Single(b => b.PartId == piecePart.Id).Quantity.Should().Be(3m);
        result.BuyItems.Single(b => b.PartId == rollPart.Id).Quantity.Should().Be(1m);
        result.BuyItems.Single(b => b.PartId == wirePart.Id).Quantity.Should().Be(1.25m);
    }

    [Fact]
    public async Task Explode_UsesThePinnedBomRevision_NotTheLiveBom()
    {
        var (parentPart, parentJob) = await SeedParentAsync(buildQty: 10m);
        var pinnedChild = await SeedPartAsync("PIN-1", uom: null);
        var liveChild = await SeedPartAsync("LIVE-1", uom: null);

        var revision = new BomRevision
        {
            PartId = parentPart.Id,
            RevisionNumber = 1,
            EffectiveDate = ParentDue.AddMonths(-1),
            Entries =
            [
                new BomRevisionLine { PartId = pinnedChild.Id, Quantity = 4, SourceType = BOMSourceType.Make, UnitOfMeasure = "Each", SortOrder = 1 },
            ],
        };
        _db.Set<BomRevision>().Add(revision);
        _db.BOMLines.Add(new BOMLine { ParentPartId = parentPart.Id, ChildPartId = liveChild.Id, Quantity = 7, SourceType = BOMSourceType.Make, SortOrder = 1 });
        await _db.SaveChangesAsync();

        parentJob.BomRevisionIdAtRelease = revision.Id;
        await _db.SaveChangesAsync();

        var result = await _handler.Handle(new ExplodeJobBomCommand(parentJob.Id), CancellationToken.None);

        result.CreatedJobs.Should().ContainSingle();
        result.CreatedJobs[0].PartId.Should().Be(pinnedChild.Id);
        result.CreatedJobs[0].Quantity.Should().Be(40m);
    }

    [Fact]
    public async Task Explode_Twice_RefusesTheSecondCallWithoutCreatingDuplicates()
    {
        var (parentPart, parentJob) = await SeedParentAsync(buildQty: 5m);
        var makePart = await SeedPartAsync("MK-3", uom: null);
        _db.BOMLines.Add(new BOMLine { ParentPartId = parentPart.Id, ChildPartId = makePart.Id, Quantity = 1, SourceType = BOMSourceType.Make, SortOrder = 1 });
        await _db.SaveChangesAsync();

        await _handler.Handle(new ExplodeJobBomCommand(parentJob.Id), CancellationToken.None);

        var act = () => _handler.Handle(new ExplodeJobBomCommand(parentJob.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("This work order has already been exploded.*");
        (await _db.Jobs.CountAsync(j => j.ParentJobId == parentJob.Id)).Should().Be(1);
    }

    [Fact]
    public async Task Explode_StockOnlyBomTwice_RefusesTheSecondCallWithoutReservingAgain()
    {
        var (parentPart, parentJob) = await SeedParentAsync(buildQty: 4m);
        var stockPart = await SeedPartAsync("SK-2", uom: null);
        _db.BOMLines.Add(new BOMLine { ParentPartId = parentPart.Id, ChildPartId = stockPart.Id, Quantity = 2, SourceType = BOMSourceType.Stock, SortOrder = 1 });
        await SeedStockAsync(stockPart, 100m);
        await _db.SaveChangesAsync();

        await _handler.Handle(new ExplodeJobBomCommand(parentJob.Id), CancellationToken.None);

        var act = () => _handler.Handle(new ExplodeJobBomCommand(parentJob.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("This work order has already been exploded.*");
        (await _db.Set<Reservation>().CountAsync(r => r.JobId == parentJob.Id)).Should().Be(1);
        (await _db.BinContents.SingleAsync(b => b.EntityId == stockPart.Id)).ReservedQuantity.Should().Be(8m);
    }

    [Fact]
    public async Task Explode_AfterTheReservationsAreReleased_ReservesAgain()
    {
        var (parentPart, parentJob) = await SeedParentAsync(buildQty: 4m);
        var stockPart = await SeedPartAsync("SK-3", uom: null);
        _db.BOMLines.Add(new BOMLine { ParentPartId = parentPart.Id, ChildPartId = stockPart.Id, Quantity = 2, SourceType = BOMSourceType.Stock, SortOrder = 1 });
        await SeedStockAsync(stockPart, 100m);
        await _db.SaveChangesAsync();

        await _handler.Handle(new ExplodeJobBomCommand(parentJob.Id), CancellationToken.None);

        var reservation = await _db.Set<Reservation>().SingleAsync(r => r.JobId == parentJob.Id);
        reservation.DeletedAt = ParentDue;
        var bin = await _db.BinContents.SingleAsync(b => b.EntityId == stockPart.Id);
        bin.ReservedQuantity -= reservation.Quantity;
        await _db.SaveChangesAsync();

        var result = await _handler.Handle(new ExplodeJobBomCommand(parentJob.Id), CancellationToken.None);

        result.StockItems.Single().ReservedQuantity.Should().Be(8m);
        bin.ReservedQuantity.Should().Be(8m);
    }

    [Fact]
    public async Task Explode_AfterSubJobsAreMarkedEnteredInError_ExplodesAgainAtTheNewQuantity()
    {
        var (parentPart, parentJob) = await SeedParentAsync(buildQty: 500m);
        var makePart = await SeedPartAsync("MK-6", uom: null);
        _db.BOMLines.Add(new BOMLine { ParentPartId = parentPart.Id, ChildPartId = makePart.Id, Quantity = 2, SourceType = BOMSourceType.Make, SortOrder = 1 });
        await _db.SaveChangesAsync();

        await _handler.Handle(new ExplodeJobBomCommand(parentJob.Id), CancellationToken.None);

        var firstChild = await _db.Jobs.SingleAsync(j => j.ParentJobId == parentJob.Id);
        firstChild.Disposition = JobDisposition.EnteredInError;
        firstChild.IsArchived = true;
        (await _db.Set<JobPart>().SingleAsync(jp => jp.JobId == parentJob.Id)).Quantity = 800m;
        await _db.SaveChangesAsync();

        var result = await _handler.Handle(new ExplodeJobBomCommand(parentJob.Id), CancellationToken.None);

        result.CreatedJobs.Single().Quantity.Should().Be(1600m);
        (await _db.Jobs.CountAsync(j => j.ParentJobId == parentJob.Id && j.Disposition == null)).Should().Be(1);
    }

    [Fact]
    public async Task Explode_WithOnlyOneSubJobMarkedEnteredInError_StillRefuses()
    {
        var (parentPart, parentJob) = await SeedParentAsync(buildQty: 5m);
        var firstPart = await SeedPartAsync("MK-7", uom: null);
        var secondPart = await SeedPartAsync("MK-8", uom: null);
        _db.BOMLines.AddRange(
            new BOMLine { ParentPartId = parentPart.Id, ChildPartId = firstPart.Id, Quantity = 1, SourceType = BOMSourceType.Make, SortOrder = 1 },
            new BOMLine { ParentPartId = parentPart.Id, ChildPartId = secondPart.Id, Quantity = 1, SourceType = BOMSourceType.Make, SortOrder = 2 });
        await _db.SaveChangesAsync();

        await _handler.Handle(new ExplodeJobBomCommand(parentJob.Id), CancellationToken.None);

        var child = await _db.Jobs.FirstAsync(j => j.ParentJobId == parentJob.Id && j.PartId == firstPart.Id);
        child.Disposition = JobDisposition.EnteredInError;
        child.IsArchived = true;
        await _db.SaveChangesAsync();

        var act = () => _handler.Handle(new ExplodeJobBomCommand(parentJob.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("This work order has already been exploded.*");
    }

    [Fact]
    public async Task Explode_WithAReservationMadeByHand_IsNotTreatedAsAlreadyExploded()
    {
        var (parentPart, parentJob) = await SeedParentAsync(buildQty: 4m);
        var stockPart = await SeedPartAsync("SK-4", uom: null);
        _db.BOMLines.Add(new BOMLine { ParentPartId = parentPart.Id, ChildPartId = stockPart.Id, Quantity = 2, SourceType = BOMSourceType.Stock, SortOrder = 1 });
        await SeedStockAsync(stockPart, 100m);
        await _db.SaveChangesAsync();

        var bin = await _db.BinContents.SingleAsync(b => b.EntityId == stockPart.Id);
        _db.Set<Reservation>().Add(new Reservation { PartId = stockPart.Id, BinContentId = bin.Id, JobId = parentJob.Id, Quantity = 10m, Notes = "Held for the first article" });
        bin.ReservedQuantity = 10m;
        await _db.SaveChangesAsync();

        var result = await _handler.Handle(new ExplodeJobBomCommand(parentJob.Id), CancellationToken.None);

        result.StockItems.Single().ReservedQuantity.Should().Be(8m);
        bin.ReservedQuantity.Should().Be(18m);
    }

    [Fact]
    public async Task Explode_Twice_ReturnsConflictThroughTheMiddleware()
    {
        var (parentPart, parentJob) = await SeedParentAsync(buildQty: 5m);
        var makePart = await SeedPartAsync("MK-4", uom: null);
        _db.BOMLines.Add(new BOMLine { ParentPartId = parentPart.Id, ChildPartId = makePart.Id, Quantity = 1, SourceType = BOMSourceType.Make, SortOrder = 1 });
        await _db.SaveChangesAsync();

        var middleware = new ExceptionHandlingMiddleware(
            async _ => await _handler.Handle(new ExplodeJobBomCommand(parentJob.Id), CancellationToken.None),
            NullLogger<ExceptionHandlingMiddleware>.Instance);

        var first = new DefaultHttpContext();
        await middleware.InvokeAsync(first);
        var second = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        await middleware.InvokeAsync(second);

        first.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        second.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public async Task Explode_LogsActivityOnParentAndChild()
    {
        var (parentPart, parentJob) = await SeedParentAsync(buildQty: 2m);
        var makePart = await SeedPartAsync("MK-5", uom: null);
        _db.BOMLines.Add(new BOMLine { ParentPartId = parentPart.Id, ChildPartId = makePart.Id, Quantity = 1, SourceType = BOMSourceType.Make, SortOrder = 1 });
        await _db.SaveChangesAsync();

        var result = await _handler.Handle(new ExplodeJobBomCommand(parentJob.Id), CancellationToken.None);

        var childId = result.CreatedJobs.Single().JobId;
        (await _db.JobActivityLogs.AnyAsync(l => l.JobId == parentJob.Id && l.Action == ActivityAction.BomExploded)).Should().BeTrue();
        (await _db.JobActivityLogs.AnyAsync(l => l.JobId == childId && l.Action == ActivityAction.Created)).Should().BeTrue();
    }

    private async Task<(Part parentPart, Job parentJob)> SeedParentAsync(decimal? buildQty)
    {
        var track = new TrackType { Name = "Production", Code = "production", IsActive = true };
        _db.TrackTypes.Add(track);
        await _db.SaveChangesAsync();

        var stage = new JobStage { TrackTypeId = track.Id, Name = "Stage 1", Code = "s1", SortOrder = 1 };
        _db.JobStages.Add(stage);

        var parentPart = new Part { PartNumber = $"ASM-{Guid.NewGuid():N}"[..12], Name = "Assembly" };
        _db.Parts.Add(parentPart);
        await _db.SaveChangesAsync();

        var parentJob = new Job
        {
            JobNumber = $"J-P-{Guid.NewGuid():N}"[..12],
            Title = "Assembly build",
            TrackTypeId = track.Id,
            CurrentStageId = stage.Id,
            PartId = parentPart.Id,
            Priority = JobPriority.Urgent,
            DueDate = ParentDue,
        };
        _db.Jobs.Add(parentJob);
        await _db.SaveChangesAsync();

        if (buildQty is decimal qty)
        {
            _db.Set<JobPart>().Add(new JobPart { JobId = parentJob.Id, PartId = parentPart.Id, Quantity = qty });
            await _db.SaveChangesAsync();
        }

        return (parentPart, parentJob);
    }

    private async Task<UnitOfMeasure> SeedUomAsync(string code, string name, UomCategory category, int decimalPlaces)
    {
        var uom = new UnitOfMeasure { Code = code, Name = name, Category = category, DecimalPlaces = decimalPlaces, IsActive = true };
        _db.Set<UnitOfMeasure>().Add(uom);
        await _db.SaveChangesAsync();
        return uom;
    }

    private async Task<Part> SeedPartAsync(string partNumber, UnitOfMeasure? uom)
    {
        var part = new Part { PartNumber = partNumber, Name = partNumber, StockUomId = uom?.Id };
        _db.Parts.Add(part);
        await _db.SaveChangesAsync();
        return part;
    }

    private async Task SeedStockAsync(Part part, decimal quantity)
    {
        var location = new StorageLocation { Name = $"Bin {part.PartNumber}" };
        _db.StorageLocations.Add(location);
        await _db.SaveChangesAsync();

        _db.BinContents.Add(new BinContent
        {
            LocationId = location.Id,
            EntityType = "part",
            EntityId = part.Id,
            Quantity = quantity,
            ReservedQuantity = 0,
            PlacedBy = 1,
            PlacedAt = ParentDue.AddDays(-30),
        });
    }
}
