using System.Security.Claims;

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Capabilities;
using Forge.Api.Features.Inventory;
using Forge.Api.Features.Lots;
using Forge.Api.Features.PurchaseOrders;
using Forge.Api.Features.Quality;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Inventory;

public class ReceivingInspectionStockHoldTests
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class FakeCapabilities(params string[] enabled) : ICapabilitySnapshotProvider
    {
        public CapabilitySnapshot Current { get; } = new(
            enabled.ToDictionary(code => code, _ => true, StringComparer.Ordinal), DateTimeOffset.UtcNow);
        public bool IsEnabled(string code) => Current.IsEnabled(code);
        public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private const int UserId = 7;

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly FixedClock _clock = new();
    private readonly Mock<IHttpContextAccessor> _http = new();
    private readonly FakeCapabilities _capabilities = new(ReceivingInspectionPolicy.Capability, ReceiptLotRecords.Capability);

    public ReceivingInspectionStockHoldTests()
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, UserId.ToString()), new Claim(ClaimTypes.Name, "Receiver")], "Test");
        _http.Setup(a => a.HttpContext).Returns(new DefaultHttpContext { User = new ClaimsPrincipal(identity) });
    }

    private sealed record Seed(Part Part, PurchaseOrder Po, PurchaseOrderLine Line, StorageLocation Bin);

    private async Task<Seed> SeedAsync(
        bool requiresInspection = true,
        TraceabilityType traceability = TraceabilityType.Lot,
        decimal ordered = 100m)
    {
        var vendor = new Vendor { CompanyName = "Resin Supply" };
        var part = new Part
        {
            PartNumber = $"RS-{Guid.NewGuid():N}"[..12],
            Description = "Resin",
            RequiresReceivingInspection = requiresInspection,
            TraceabilityType = traceability,
        };
        var bin = new StorageLocation { Name = "RC-1", LocationType = LocationType.Bin, IsActive = true };
        _db.AddRange(vendor, part, bin);
        await _db.SaveChangesAsync();

        var po = new PurchaseOrder { PONumber = $"PO-{Guid.NewGuid():N}"[..10], VendorId = vendor.Id, Status = PurchaseOrderStatus.Submitted };
        _db.PurchaseOrders.Add(po);
        await _db.SaveChangesAsync();

        var line = new PurchaseOrderLine
        {
            PurchaseOrderId = po.Id,
            PartId = part.Id,
            Description = "Resin",
            OrderedQuantity = ordered,
            UnitPrice = 2m,
        };
        _db.PurchaseOrderLines.Add(line);
        await _db.SaveChangesAsync();
        return new Seed(part, po, line, bin);
    }

    private Task ReceiveItemsAsync(Seed seed, decimal quantity, string? lot, ICapabilitySnapshotProvider? capabilities = null)
        => new ReceiveItemsHandler(
                new PurchaseOrderRepository(_db), _clock, Mock.Of<MediatR.IMediator>(), _http.Object,
                _db, null, new InventoryRepository(_db), capabilities ?? _capabilities, Mock.Of<IBarcodeService>())
            .Handle(new ReceiveItemsCommand(seed.Po.Id,
                [new ReceiveLineModel(seed.Line.Id, quantity, seed.Bin.Id, null, LotNumber: lot)]), CancellationToken.None);

    private Task<ReceivingRecordResponseModel> ReceivePurchaseOrderAsync(Seed seed, decimal quantity, string? lot)
        => new ReceivePurchaseOrderHandler(
                new PurchaseOrderRepository(_db), new InventoryRepository(_db), _http.Object, _clock,
                _db, null, _capabilities, Mock.Of<IBarcodeService>())
            .Handle(new ReceivePurchaseOrderCommand(
                new ReceivePurchaseOrderRequestModel(seed.Line.Id, quantity, seed.Bin.Id, lot, null)), CancellationToken.None);

    private Task RecordAsync(int receivingRecordId, string result, decimal accepted, decimal rejected, bool createNcr = true)
        => new RecordInspectionResultHandler(_db, _http.Object, new NcrCapaService(_db, _clock), _clock)
            .Handle(new RecordInspectionResultCommand(receivingRecordId,
                new InspectionResultRequestModel(result, accepted, rejected, null, createNcr)), CancellationToken.None);

    private Task WaiveAsync(int receivingRecordId)
        => new WaiveInspectionHandler(_db, _http.Object, _clock)
            .Handle(new WaiveInspectionCommand(receivingRecordId, "Certified supplier"), CancellationToken.None);

    private Task<List<BinContent>> RowsAsync(int partId)
        => _db.BinContents.AsNoTracking()
            .Where(bc => bc.EntityType == "part" && bc.EntityId == partId && bc.RemovedAt == null)
            .OrderBy(bc => bc.Id)
            .ToListAsync();

    private async Task<decimal> QuantityAsync(int partId, BinContentStatus status)
        => (await RowsAsync(partId)).Where(r => r.Status == status).Sum(r => r.Quantity);

    private Task<List<ReceivingRecord>> ReceiptsAsync(int lineId)
        => _db.ReceivingRecords.AsNoTracking().Where(r => r.PurchaseOrderLineId == lineId).OrderBy(r => r.Id).ToListAsync();

    [Fact]
    public async Task ReceiveItems_PendingInspection_StocksOnQcHold()
    {
        var seed = await SeedAsync();

        await ReceiveItemsAsync(seed, 30m, "R-26-117");

        (await ReceiptsAsync(seed.Line.Id)).Single().InspectionStatus.Should().Be(ReceivingInspectionStatus.Pending);
        var row = (await RowsAsync(seed.Part.Id)).Should().ContainSingle().Subject;
        row.Status.Should().Be(BinContentStatus.QcHold);
        row.Quantity.Should().Be(30m);
        row.LotNumber.Should().Be("R-26-117");
    }

    [Fact]
    public async Task ReceiveItems_NoInspectionRequired_StocksStored()
    {
        var seed = await SeedAsync(requiresInspection: false);

        await ReceiveItemsAsync(seed, 30m, "H-55821");

        (await RowsAsync(seed.Part.Id)).Should().ContainSingle().Which.Status.Should().Be(BinContentStatus.Stored);
    }

    [Fact]
    public async Task ReceiveItems_PendingReceiptOfAStockedLot_DoesNotMergeIntoTheStoredRow()
    {
        var seed = await SeedAsync();
        _db.BinContents.Add(new BinContent
        {
            LocationId = seed.Bin.Id, EntityType = "part", EntityId = seed.Part.Id, Quantity = 12m,
            LotNumber = "R-26-117", Status = BinContentStatus.Stored, PlacedAt = _clock.UtcNow,
        });
        await _db.SaveChangesAsync();

        await ReceiveItemsAsync(seed, 30m, "R-26-117");

        (await QuantityAsync(seed.Part.Id, BinContentStatus.Stored)).Should().Be(12m);
        (await QuantityAsync(seed.Part.Id, BinContentStatus.QcHold)).Should().Be(30m);
    }

    [Fact]
    public async Task ReceivePurchaseOrder_PendingInspection_StocksOnQcHold()
    {
        var seed = await SeedAsync();

        await ReceivePurchaseOrderAsync(seed, 20m, "R-26-118");

        (await RowsAsync(seed.Part.Id)).Should().ContainSingle().Which.Status.Should().Be(BinContentStatus.QcHold);
    }

    [Fact]
    public async Task Pass_MovesTheAcceptedQuantityToStored_AndLeavesAnotherPendingReceiptHeld()
    {
        var seed = await SeedAsync();
        await ReceiveItemsAsync(seed, 30m, "R-26-117");
        await ReceiveItemsAsync(seed, 20m, "R-26-117");
        var receipts = await ReceiptsAsync(seed.Line.Id);
        (await QuantityAsync(seed.Part.Id, BinContentStatus.QcHold)).Should().Be(50m);

        await RecordAsync(receipts[0].Id, "Passed", 30m, 0m);

        (await QuantityAsync(seed.Part.Id, BinContentStatus.Stored)).Should().Be(30m);
        (await QuantityAsync(seed.Part.Id, BinContentStatus.QcHold)).Should().Be(20m);
        var movement = await _db.BinMovements.SingleAsync(m => m.Reason == BinMovementReason.QcRelease);
        movement.Quantity.Should().Be(30m);
        movement.LotNumber.Should().Be("R-26-117");
        movement.FromLocationId.Should().Be(seed.Bin.Id);
        movement.ToLocationId.Should().Be(seed.Bin.Id);
    }

    [Fact]
    public async Task Fail_KeepsTheRejectedQuantityHeld_AndTheNcrCarriesTheLot()
    {
        var seed = await SeedAsync();
        await ReceiveItemsAsync(seed, 33m, "R-26-118");
        var receipt = (await ReceiptsAsync(seed.Line.Id)).Single();

        await RecordAsync(receipt.Id, "Failed", 0m, 33m);

        (await QuantityAsync(seed.Part.Id, BinContentStatus.QcHold)).Should().Be(33m);
        (await QuantityAsync(seed.Part.Id, BinContentStatus.Stored)).Should().Be(0m);
        (await _db.NonConformances.SingleAsync()).LotNumber.Should().Be("R-26-118");
    }

    [Fact]
    public async Task PartialAccept_ReleasesOnlyTheAcceptedQuantity()
    {
        var seed = await SeedAsync();
        await ReceiveItemsAsync(seed, 10m, "R-26-119");
        var receipt = (await ReceiptsAsync(seed.Line.Id)).Single();

        await RecordAsync(receipt.Id, "PartialAccept", 8m, 2m);

        (await QuantityAsync(seed.Part.Id, BinContentStatus.Stored)).Should().Be(8m);
        (await QuantityAsync(seed.Part.Id, BinContentStatus.QcHold)).Should().Be(2m);
    }

    [Fact]
    public async Task PartialAccept_InPurchaseUnits_ReleasesTheBaseQuantity()
    {
        var seed = await SeedAsync();
        var unit = new PartPurchaseUnit { PartId = seed.Part.Id, Label = "Bag", ContentQuantity = 25m };
        _db.Add(unit);
        await _db.SaveChangesAsync();
        seed.Line.PurchaseUnitId = unit.Id;
        await _db.SaveChangesAsync();
        await ReceiveItemsAsync(seed, 4m, "R-26-120");
        var receipt = (await ReceiptsAsync(seed.Line.Id)).Single();
        (await QuantityAsync(seed.Part.Id, BinContentStatus.QcHold)).Should().Be(100m);

        await RecordAsync(receipt.Id, "PartialAccept", 3m, 1m);

        (await QuantityAsync(seed.Part.Id, BinContentStatus.Stored)).Should().Be(75m);
        (await QuantityAsync(seed.Part.Id, BinContentStatus.QcHold)).Should().Be(25m);
    }

    [Fact]
    public async Task Waive_ReleasesTheWholeReceipt()
    {
        var seed = await SeedAsync();
        await ReceiveItemsAsync(seed, 30m, "R-26-121");
        var receipt = (await ReceiptsAsync(seed.Line.Id)).Single();

        await WaiveAsync(receipt.Id);

        (await RowsAsync(seed.Part.Id)).Should().ContainSingle().Which.Status.Should().Be(BinContentStatus.Stored);
    }

    [Fact]
    public async Task Pass_WhileAnOpenNcrHoldsTheLot_LeavesTheStockHeld()
    {
        var seed = await SeedAsync();
        await ReceiveItemsAsync(seed, 30m, "R-26-122");
        var receipt = (await ReceiptsAsync(seed.Line.Id)).Single();
        _db.NonConformances.Add(new NonConformance
        {
            NcrNumber = "NCR-1", Type = NcrType.Supplier, PartId = seed.Part.Id, LotNumber = "R-26-122",
            Status = NcrStatus.Open, Description = "Splay", DetectedById = UserId, DetectedAt = _clock.UtcNow,
        });
        await _db.SaveChangesAsync();

        await RecordAsync(receipt.Id, "Passed", 30m, 0m);

        (await QuantityAsync(seed.Part.Id, BinContentStatus.QcHold)).Should().Be(30m);
    }

    [Fact]
    public async Task LotHoldRelease_LeavesHeldTheQuantityStillAwaitingInspection()
    {
        var seed = await SeedAsync();
        await ReceiveItemsAsync(seed, 10m, "R-26-123");
        var first = (await ReceiptsAsync(seed.Line.Id)).Single();
        await RecordAsync(first.Id, "Failed", 0m, 10m);
        await ReceiveItemsAsync(seed, 15m, "R-26-123");
        (await QuantityAsync(seed.Part.Id, BinContentStatus.QcHold)).Should().Be(25m);

        var ncr = await _db.NonConformances.SingleAsync();
        ncr.Status = NcrStatus.Closed;
        await LotQualityHold.ReleaseAsync(_db, ncr, CancellationToken.None);
        await _db.SaveChangesAsync();

        (await QuantityAsync(seed.Part.Id, BinContentStatus.Stored)).Should().Be(10m);
        (await QuantityAsync(seed.Part.Id, BinContentStatus.QcHold)).Should().Be(15m);
    }

    [Fact]
    public async Task Receipt_OfALotTrackedPart_CreatesItsLotRecord()
    {
        var seed = await SeedAsync(requiresInspection: false);

        await ReceiveItemsAsync(seed, 30m, "H-55821");
        await ReceiveItemsAsync(seed, 20m, "H-55821");

        var lot = await _db.LotRecords.SingleAsync();
        lot.LotNumber.Should().Be("H-55821");
        lot.PartId.Should().Be(seed.Part.Id);
        lot.PurchaseOrderLineId.Should().Be(seed.Line.Id);
        lot.Quantity.Should().Be(50m);
    }

    [Fact]
    public async Task ReceivePurchaseOrder_OfALotTrackedPart_CreatesItsLotRecord()
    {
        var seed = await SeedAsync(requiresInspection: false);

        await ReceivePurchaseOrderAsync(seed, 12m, "H-55822");

        (await _db.LotRecords.SingleAsync()).Quantity.Should().Be(12m);
    }

    [Fact]
    public async Task Receipt_OfAPartWithoutLotTracking_CreatesNoLotRecord()
    {
        var seed = await SeedAsync(requiresInspection: false, traceability: TraceabilityType.None);

        await ReceiveItemsAsync(seed, 30m, "H-1");

        (await _db.LotRecords.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Receipt_WithLotsOff_CreatesNoLotRecord()
    {
        var seed = await SeedAsync(requiresInspection: false);

        await ReceiveItemsAsync(seed, 30m, "H-2", new FakeCapabilities());

        (await _db.LotRecords.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Receipt_WhoseLotNumberBelongsToAnotherPart_StillReceives_AndSaysSo()
    {
        var seed = await SeedAsync(requiresInspection: false);
        var other = new Part { PartNumber = "OTHER-1", Description = "Other" };
        _db.Parts.Add(other);
        await _db.SaveChangesAsync();
        _db.LotRecords.Add(new LotRecord { LotNumber = "SHARED-7", PartId = other.Id, Quantity = 5m });
        await _db.SaveChangesAsync();

        await ReceiveItemsAsync(seed, 30m, "SHARED-7");

        (await _db.LotRecords.SingleAsync()).PartId.Should().Be(other.Id);
        (await QuantityAsync(seed.Part.Id, BinContentStatus.Stored)).Should().Be(30m);
        (await _db.ActivityLogs.AnyAsync(l => l.Action == "lot-record-skipped" && l.EntityId == seed.Po.Id)).Should().BeTrue();
    }
}
