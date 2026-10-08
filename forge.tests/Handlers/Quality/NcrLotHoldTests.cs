using System.Security.Claims;

using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Features.Inventory;
using Forge.Api.Features.Jobs;
using Forge.Api.Features.Mobile;
using Forge.Api.Features.Quality;
using Forge.Api.Features.Scanner;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Quality;

public class NcrLotHoldTests : IDisposable
{
    private const int UserId = 7;
    private const string HeldLot = "LOT-X";
    private const string OtherLot = "LOT-Y";
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly IClock _clock = Mock.Of<IClock>(c => c.UtcNow == Now);
    private readonly IHttpContextAccessor _accessor;

    private Part _part = null!;
    private StorageLocation _binA = null!;
    private StorageLocation _binB = null!;
    private LotRecord _heldLotRecord = null!;

    public NcrLotHoldTests()
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, UserId.ToString())], "Test");
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(new DefaultHttpContext { User = new ClaimsPrincipal(identity) });
        _accessor = accessor.Object;
    }

    public void Dispose() => _db.Dispose();

    private async Task SeedAsync()
    {
        _part = new Part { PartNumber = "BRK-100", Name = "Bracket", Revision = "C" };
        _db.Parts.Add(_part);
        _binA = new StorageLocation { Name = "A1", LocationType = LocationType.Bin, IsActive = true };
        _binB = new StorageLocation { Name = "B1", LocationType = LocationType.Bin, IsActive = true };
        _db.StorageLocations.AddRange(_binA, _binB);
        await _db.SaveChangesAsync();

        _heldLotRecord = new LotRecord { LotNumber = HeldLot, PartId = _part.Id, Quantity = 30 };
        _db.LotRecords.Add(_heldLotRecord);
        await _db.SaveChangesAsync();
    }

    private async Task<BinContent> StockAsync(
        StorageLocation location, string? lot, decimal quantity, int? partId = null, int daysAgo = 1)
    {
        var row = new BinContent
        {
            LocationId = location.Id,
            EntityType = "part",
            EntityId = partId ?? _part.Id,
            Quantity = quantity,
            LotNumber = lot,
            Status = BinContentStatus.Stored,
            PlacedAt = Now.AddDays(-daysAgo),
        };
        _db.BinContents.Add(row);
        await _db.SaveChangesAsync();
        return row;
    }

    private async Task<NcrResponseModel> RaiseNcrAsync(string? lot = HeldLot)
        => await new CreateNcrHandler(_db, new NcrCapaService(_db, _clock), _clock, _accessor).Handle(
            new CreateNcrCommand(new CreateNcrRequestModel
            {
                Type = NcrType.Internal,
                PartId = _part.Id,
                LotNumber = lot,
                DetectedAtStage = NcrDetectionStage.InProcess,
                Description = "Holes off location",
                AffectedQuantity = 10,
            }),
            CancellationToken.None);

    private Task DispositionAsync(int ncrId, NcrDispositionCode code)
        => new DispositionNcrHandler(_db, _clock, _accessor).Handle(
            new DispositionNcrCommand(ncrId, new DispositionNcrRequestModel
            {
                Code = code,
                Notes = "Reviewed by quality",
                ReworkInstructions = code == NcrDispositionCode.Rework ? "Re-drill" : null,
            }),
            CancellationToken.None);

    private Task CloseAsync(int ncrId)
        => new CloseNcrHandler(_db).Handle(new CloseNcrCommand(ncrId, new CloseNcrRequestModel()), CancellationToken.None);

    private async Task<BinContentStatus> StatusOf(int binContentId)
        => (await _db.BinContents.AsNoTracking().SingleAsync(b => b.Id == binContentId)).Status;

    private static string HoldMessage(NcrResponseModel ncr) => $"Lot {HeldLot} is on quality hold (NCR {ncr.NcrNumber}).";

    private async Task<Job> JobAsync()
    {
        var job = new Job { JobNumber = "JOB-500", Description = "Weldment" };
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();
        return job;
    }

    [Fact]
    public async Task Create_HoldsStoredContentsOfThePartAndLotOnly_AndRecordsTheRevision()
    {
        await SeedAsync();
        var otherPart = new Part { PartNumber = "BRK-200", Name = "Other bracket" };
        _db.Parts.Add(otherPart);
        await _db.SaveChangesAsync();
        var heldA = await StockAsync(_binA, HeldLot, 10);
        var heldB = await StockAsync(_binB, HeldLot, 5);
        var otherLot = await StockAsync(_binA, OtherLot, 8);
        var otherPartSameLot = await StockAsync(_binA, HeldLot, 4, otherPart.Id);

        var ncr = await RaiseNcrAsync();

        (await StatusOf(heldA.Id)).Should().Be(BinContentStatus.QcHold);
        (await StatusOf(heldB.Id)).Should().Be(BinContentStatus.QcHold);
        (await StatusOf(otherLot.Id)).Should().Be(BinContentStatus.Stored);
        (await StatusOf(otherPartSameLot.Id)).Should().Be(BinContentStatus.Stored);

        var saved = await _db.NonConformances.AsNoTracking().SingleAsync(n => n.Id == ncr.Id);
        saved.PartRevision.Should().Be("C");

        var logs = await _db.ActivityLogs.AsNoTracking().Where(a => a.Action == "quality-hold-placed").ToListAsync();
        logs.Select(a => (a.EntityType, a.EntityId)).Should().BeEquivalentTo(
            new[] { ("NonConformance", ncr.Id), ("Lot", _heldLotRecord.Id) });
    }

    [Fact]
    public async Task Create_WithoutALot_HoldsNothing()
    {
        await SeedAsync();
        var row = await StockAsync(_binA, HeldLot, 10);

        await RaiseNcrAsync(lot: null);

        (await StatusOf(row.Id)).Should().Be(BinContentStatus.Stored);
        (await _db.ActivityLogs.AnyAsync(a => a.Action == "quality-hold-placed")).Should().BeFalse();
    }

    [Theory]
    [InlineData(NcrDispositionCode.UseAsIs)]
    [InlineData(NcrDispositionCode.Rework)]
    public async Task UseAsIsOrRework_ReleasesTheHold(NcrDispositionCode code)
    {
        await SeedAsync();
        var row = await StockAsync(_binA, HeldLot, 10);
        var ncr = await RaiseNcrAsync();

        await DispositionAsync(ncr.Id, code);

        (await StatusOf(row.Id)).Should().Be(BinContentStatus.Stored);
        var logs = await _db.ActivityLogs.AsNoTracking().Where(a => a.Action == "quality-hold-released").ToListAsync();
        logs.Select(a => (a.EntityType, a.EntityId)).Should().BeEquivalentTo(
            new[] { ("NonConformance", ncr.Id), ("Lot", _heldLotRecord.Id) });
    }

    [Theory]
    [InlineData(NcrDispositionCode.Scrap)]
    [InlineData(NcrDispositionCode.ReturnToVendor)]
    public async Task ScrapOrReturnToVendor_KeepsTheHold_UntilClose(NcrDispositionCode code)
    {
        await SeedAsync();
        var row = await StockAsync(_binA, HeldLot, 10);
        var ncr = await RaiseNcrAsync();

        await DispositionAsync(ncr.Id, code);
        (await StatusOf(row.Id)).Should().Be(BinContentStatus.QcHold);

        await CloseAsync(ncr.Id);
        (await StatusOf(row.Id)).Should().Be(BinContentStatus.Stored);
    }

    [Fact]
    public async Task Close_KeepsTheHold_WhenAnActiveRecallCoversTheLot()
    {
        await SeedAsync();
        var row = await StockAsync(_binA, HeldLot, 10);
        var ncr = await RaiseNcrAsync();
        var recall = new Recall
        {
            InitiatedByUserId = UserId,
            InitiatedLotId = _heldLotRecord.Id,
            Reason = "Supplier notice",
            RecallDate = Now,
            Status = RecallStatus.Active,
        };
        recall.AffectedLots.Add(new RecallAffectedLot { LotId = _heldLotRecord.Id });
        _db.Recalls.Add(recall);
        await _db.SaveChangesAsync();

        await DispositionAsync(ncr.Id, NcrDispositionCode.Scrap);
        await CloseAsync(ncr.Id);

        (await StatusOf(row.Id)).Should().Be(BinContentStatus.QcHold);
        (await _db.ActivityLogs.AnyAsync(a => a.Action == "quality-hold-released")).Should().BeFalse();
    }

    [Fact]
    public async Task UseAsIs_KeepsTheHold_WhileAnotherNcrStillHoldsTheLot()
    {
        await SeedAsync();
        var row = await StockAsync(_binA, HeldLot, 10);
        var first = await RaiseNcrAsync();
        var second = await RaiseNcrAsync();

        await DispositionAsync(first.Id, NcrDispositionCode.UseAsIs);

        (await StatusOf(row.Id)).Should().Be(BinContentStatus.QcHold);
        var act = () => new TransferStockHandler(new InventoryRepository(_db), _accessor, _clock).Handle(
            new TransferStockCommand(new TransferStockRequestModel(row.Id, _binB.Id, 1, null)), CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage(HoldMessage(second));
    }

    [Fact]
    public async Task MaterialIssue_OfTheHeldLotIsRefused_OtherLotsStillIssue_AndUseAsIsReleases()
    {
        await SeedAsync();
        var job = await JobAsync();
        var held = await StockAsync(_binA, HeldLot, 10);
        var other = await StockAsync(_binA, OtherLot, 10);
        var ncr = await RaiseNcrAsync();
        var handler = new CreateMaterialIssueHandler(_db);
        CreateMaterialIssueCommand Issue(BinContent bin) => new(
            job.Id, _part.Id, null, 3, bin.Id, bin.LocationId, bin.LotNumber, MaterialIssueType.Issue, null, UserId);

        var refused = () => handler.Handle(Issue(held), CancellationToken.None);
        await refused.Should().ThrowAsync<InvalidOperationException>().WithMessage(HoldMessage(ncr));

        await handler.Handle(Issue(other), CancellationToken.None);
        (await _db.BinContents.AsNoTracking().SingleAsync(b => b.Id == other.Id)).Quantity.Should().Be(7);

        await DispositionAsync(ncr.Id, NcrDispositionCode.UseAsIs);
        await handler.Handle(Issue(held), CancellationToken.None);
        (await _db.BinContents.AsNoTracking().SingleAsync(b => b.Id == held.Id)).Quantity.Should().Be(7);
    }

    [Fact]
    public async Task MaterialIssue_ByLotWithoutABin_IsRefusedWhenOnlyHeldStockHasTheLot()
    {
        await SeedAsync();
        var job = await JobAsync();
        await StockAsync(_binA, HeldLot, 10);
        var ncr = await RaiseNcrAsync();

        var act = () => new CreateMaterialIssueHandler(_db).Handle(new CreateMaterialIssueCommand(
            job.Id, _part.Id, null, 3, null, null, HeldLot, MaterialIssueType.Issue, null, UserId), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage(HoldMessage(ncr));
    }

    [Fact]
    public async Task Transfer_OfHeldStockIsRefused_OtherLotsStillTransfer()
    {
        await SeedAsync();
        var held = await StockAsync(_binA, HeldLot, 10);
        var other = await StockAsync(_binA, OtherLot, 10);
        var ncr = await RaiseNcrAsync();
        var handler = new TransferStockHandler(new InventoryRepository(_db), _accessor, _clock);

        var refused = () => handler.Handle(
            new TransferStockCommand(new TransferStockRequestModel(held.Id, _binB.Id, 4, null)), CancellationToken.None);
        await refused.Should().ThrowAsync<InvalidOperationException>().WithMessage(HoldMessage(ncr));

        await handler.Handle(
            new TransferStockCommand(new TransferStockRequestModel(other.Id, _binB.Id, 4, null)), CancellationToken.None);
        (await _db.BinContents.AsNoTracking().SingleAsync(b => b.Id == other.Id)).Quantity.Should().Be(6);
        (await _db.BinContents.AsNoTracking().AnyAsync(b => b.LocationId == _binB.Id && b.LotNumber == OtherLot))
            .Should().BeTrue();
    }

    [Fact]
    public async Task UseStock_SkipsHeldStock_AndRefusesWhenOnlyHeldStockCouldCover()
    {
        await SeedAsync();
        var held = await StockAsync(_binA, HeldLot, 10, daysAgo: 5);
        var other = await StockAsync(_binA, OtherLot, 4, daysAgo: 1);
        var ncr = await RaiseNcrAsync();
        var handler = new UseStockHandler(new InventoryRepository(_db), _accessor, _clock);
        UseStockCommand Use(decimal qty) => new(new UseStockRequestModel(_part.Id, _binA.Id, qty, null, null));

        var refused = () => handler.Handle(Use(6), CancellationToken.None);
        await refused.Should().ThrowAsync<InvalidOperationException>().WithMessage(HoldMessage(ncr));

        await handler.Handle(Use(3), CancellationToken.None);
        (await _db.BinContents.AsNoTracking().SingleAsync(b => b.Id == held.Id)).Quantity.Should().Be(10);
        (await _db.BinContents.AsNoTracking().SingleAsync(b => b.Id == other.Id)).Quantity.Should().Be(1);
    }

    [Fact]
    public async Task MobileMove_OfTheHeldLotIsRefused_OtherLotsStillMove()
    {
        await SeedAsync();
        await StockAsync(_binA, HeldLot, 10);
        var other = await StockAsync(_binA, OtherLot, 10);
        var ncr = await RaiseNcrAsync();
        var mediator = new Mock<IMediator>();
        var handler = new MoveStockHandler(_db, mediator.Object);

        var refused = () => handler.Handle(
            new MoveStockCommand(_part.Id, _binA.Id, _binB.Id, 2, HeldLot, "device-1"), CancellationToken.None);
        await refused.Should().ThrowAsync<InvalidOperationException>().WithMessage(HoldMessage(ncr));
        mediator.Verify(m => m.Send(It.IsAny<TransferStockCommand>(), It.IsAny<CancellationToken>()), Times.Never);

        await handler.Handle(
            new MoveStockCommand(_part.Id, _binA.Id, _binB.Id, 2, OtherLot, "device-1"), CancellationToken.None);
        mediator.Verify(m => m.Send(
            It.Is<TransferStockCommand>(c => c.Data.SourceBinContentId == other.Id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ScanIssue_RefusesWhenOnlyHeldStockIsAtTheBin()
    {
        await SeedAsync();
        var job = await JobAsync();
        await StockAsync(_binA, HeldLot, 10);
        var ncr = await RaiseNcrAsync();

        var act = () => new ExecuteScanIssueHandler(_db, _clock, _accessor).Handle(
            new ExecuteScanIssueCommand(new ScanIssueRequestModel(_part.Id, job.Id, 2, _binA.Id)), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage(HoldMessage(ncr));
    }

    [Fact]
    public async Task ScanMove_SkipsHeldStock_AndRefusesWhenOnlyHeldStockCouldCover()
    {
        await SeedAsync();
        var held = await StockAsync(_binA, HeldLot, 10, daysAgo: 5);
        await StockAsync(_binA, OtherLot, 4, daysAgo: 1);
        var ncr = await RaiseNcrAsync();
        var handler = new ExecuteScanMoveHandler(_db, _clock, _accessor);
        ExecuteScanMoveCommand Move(decimal qty) => new(new ScanMoveRequestModel(_part.Id, _binA.Id, _binB.Id, qty));

        var refused = () => handler.Handle(Move(6), CancellationToken.None);
        await refused.Should().ThrowAsync<InvalidOperationException>().WithMessage(HoldMessage(ncr));

        await handler.Handle(Move(4), CancellationToken.None);
        (await _db.BinContents.AsNoTracking().SingleAsync(b => b.Id == held.Id)).Quantity.Should().Be(10);
        var moved = await _db.BinContents.AsNoTracking().SingleAsync(b => b.LocationId == _binB.Id);
        moved.LotNumber.Should().Be(OtherLot);
        moved.Quantity.Should().Be(4);
    }

    [Fact]
    public async Task ShippingRelief_RefusesWhenOnlyHeldStockCouldCover_AndShipsFromOtherLots()
    {
        await SeedAsync();
        var held = await StockAsync(_binA, HeldLot, 10, daysAgo: 5);
        var ncr = await RaiseNcrAsync();
        var customer = new Customer { Name = "Design partner" };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();
        var order = new SalesOrder
        {
            OrderNumber = "SO-900",
            CustomerId = customer.Id,
            Status = SalesOrderStatus.Confirmed,
            Lines = [new SalesOrderLine { PartId = _part.Id, Description = "Bracket", Quantity = 5, UnitPrice = 10, LineNumber = 1 }],
        };
        _db.SalesOrders.Add(order);
        await _db.SaveChangesAsync();
        var shipment = new Shipment
        {
            ShipmentNumber = "SH-900",
            SalesOrderId = order.Id,
            Status = ShipmentStatus.Pending,
            Lines = [new ShipmentLine { SalesOrderLineId = order.Lines.First().Id, Quantity = 5 }],
        };
        _db.Shipments.Add(shipment);
        await _db.SaveChangesAsync();
        var relief = new InventoryReliefService(_db, NullLogger<InventoryReliefService>.Instance);
        async Task<Shipment> Loaded() => await _db.Shipments
            .Include(s => s.Lines).ThenInclude(l => l.SalesOrderLine)
            .SingleAsync(s => s.Id == shipment.Id);

        var refused = async () => await relief.RelieveShipmentAsync(await Loaded(), UserId, CancellationToken.None);
        await refused.Should().ThrowAsync<InvalidOperationException>().WithMessage(HoldMessage(ncr));

        var other = await StockAsync(_binB, OtherLot, 6, daysAgo: 1);
        await relief.RelieveShipmentAsync(await Loaded(), UserId, CancellationToken.None);

        (await _db.BinContents.AsNoTracking().SingleAsync(b => b.Id == held.Id)).Quantity.Should().Be(10);
        (await _db.BinContents.AsNoTracking().SingleAsync(b => b.Id == other.Id)).Quantity.Should().Be(1);
    }
}
