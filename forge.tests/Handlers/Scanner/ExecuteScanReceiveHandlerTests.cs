using System.Security.Claims;

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Capabilities;
using Forge.Api.Features.Accounting;
using Forge.Api.Features.Lots;
using Forge.Api.Features.Scanner;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Scanner;

public class ExecuteScanReceiveHandlerTests : IDisposable
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly IClock _clock;
    private readonly IHttpContextAccessor _accessor;
    private readonly ExecuteScanReceiveHandler _handler;

    public ExecuteScanReceiveHandlerTests()
    {
        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero));
        _clock = clock.Object;
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "7"), new Claim(ClaimTypes.Name, "Receiver")], "Test")),
        });
        _accessor = accessor.Object;
        _handler = new ExecuteScanReceiveHandler(_db, _accessor, ReceiptMediator.For(_db, _clock, _accessor));
    }

    private ExecuteScanReceiveHandler HandlerWith(
        ICapabilitySnapshotProvider? capabilities = null, IReceiptInventoryPostingService? posting = null)
        => new(_db, _accessor, ReceiptMediator.For(_db, _clock, _accessor, capabilities, posting));

    private async Task<(int PartId, int LineId, int BinId)> SeedAsync(
        decimal? contentPerUnit,
        PurchaseOrderStatus poStatus = PurchaseOrderStatus.Submitted,
        TraceabilityType traceability = TraceabilityType.None)
    {
        var part = new Part { PartNumber = "BAR-12", Description = "12 ft bar", TraceabilityType = traceability };
        _db.Parts.Add(part);
        var bin = new StorageLocation { Name = "A1", LocationType = LocationType.Bin, IsActive = true };
        _db.StorageLocations.Add(bin);
        await _db.SaveChangesAsync();

        PartPurchaseUnit? unit = null;
        if (contentPerUnit is decimal content)
        {
            unit = new PartPurchaseUnit { PartId = part.Id, Label = "12 ft bar", ContentQuantity = content };
            _db.PartPurchaseUnits.Add(unit);
        }
        var vendor = new Vendor { CompanyName = "Bar stock supplier" };
        _db.Vendors.Add(vendor);
        await _db.SaveChangesAsync();
        var po = new PurchaseOrder { PONumber = "PO-SCAN-1", VendorId = vendor.Id, Status = poStatus };
        _db.PurchaseOrders.Add(po);
        await _db.SaveChangesAsync();

        var line = new PurchaseOrderLine
        {
            PurchaseOrderId = po.Id,
            PartId = part.Id,
            OrderedQuantity = 5m,
            UnitPrice = 30m,
            PurchaseUnitId = unit?.Id,
        };
        _db.PurchaseOrderLines.Add(line);
        _db.BinContents.Add(new BinContent
        {
            LocationId = bin.Id, EntityType = "part", EntityId = part.Id, Quantity = 24m, LotNumber = "HEAT-A",
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return (part.Id, line.Id, bin.Id);
    }

    [Fact]
    public async Task Handle_PurchaseUnitLine_StocksBaseUnitsAndKeepsLineInOrderUnits()
    {
        var (partId, lineId, binId) = await SeedAsync(contentPerUnit: 12m);

        await _handler.Handle(new ExecuteScanReceiveCommand(
            new ScanReceiveRequestModel(partId, lineId, 2m, binId)),
            CancellationToken.None);

        _db.ChangeTracker.Clear();
        (await _db.PurchaseOrderLines.SingleAsync(l => l.Id == lineId)).ReceivedQuantity.Should().Be(2m);
        (await _db.ReceivingRecords.SingleAsync(r => r.PurchaseOrderLineId == lineId)).QuantityReceived.Should().Be(2m);
        var unlotted = await _db.BinContents.SingleAsync(b => b.EntityId == partId && b.LotNumber == null);
        unlotted.Quantity.Should().Be(24m);
        (await _db.BinMovements.SingleAsync(m => m.EntityId == partId)).Quantity.Should().Be(24m);
        (await _db.ScanActionLogs.SingleAsync(l => l.PartId == partId)).Quantity.Should().Be(24m);
    }

    [Fact]
    public async Task Handle_BinHoldsALot_DoesNotBlendIntoTheLottedRow()
    {
        var (partId, lineId, binId) = await SeedAsync(contentPerUnit: null);

        await _handler.Handle(new ExecuteScanReceiveCommand(
            new ScanReceiveRequestModel(partId, lineId, 3m, binId)),
            CancellationToken.None);

        _db.ChangeTracker.Clear();
        var rows = await _db.BinContents.Where(b => b.EntityId == partId).ToListAsync();
        rows.Single(b => b.LotNumber == "HEAT-A").Quantity.Should().Be(24m);
        rows.Single(b => b.LotNumber == null).Quantity.Should().Be(3m);
    }

    [Fact]
    public async Task Handle_RecordsAPurchaseOrderReceipt_AndTagsTheMovementWithTheScan()
    {
        var (partId, lineId, binId) = await SeedAsync(contentPerUnit: null);
        var posting = new Mock<IReceiptInventoryPostingService>();

        var logId = await HandlerWith(posting: posting.Object).Handle(new ExecuteScanReceiveCommand(
            new ScanReceiveRequestModel(partId, lineId, 5m, binId)),
            CancellationToken.None);

        _db.ChangeTracker.Clear();
        var record = await _db.ReceivingRecords.SingleAsync(r => r.PurchaseOrderLineId == lineId);
        record.ReceiptNumber.Should().StartWith("R-");
        record.Notes.Should().Be(ScanReceiveNote.For(logId));
        record.ReceivedBy.Should().Be("Receiver");
        var line = await _db.PurchaseOrderLines.Include(l => l.PurchaseOrder).SingleAsync(l => l.Id == lineId);
        line.PurchaseOrder.Status.Should().Be(PurchaseOrderStatus.Received);
        var movement = await _db.BinMovements.SingleAsync(m => m.EntityId == partId);
        movement.Reason.Should().Be(BinMovementReason.ScanReceive);
        movement.ScanActionLogId.Should().Be(logId);
        posting.Verify(p => p.PostReceiptAsync(
            line.PurchaseOrderId, record.ReceiptNumber, It.IsAny<DateOnly>(), 7, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_DraftPurchaseOrder_IsRefused()
    {
        var (partId, lineId, binId) = await SeedAsync(contentPerUnit: null, poStatus: PurchaseOrderStatus.Draft);

        var act = () => _handler.Handle(new ExecuteScanReceiveCommand(
            new ScanReceiveRequestModel(partId, lineId, 1m, binId)),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _db.ChangeTracker.Clear();
        (await _db.ReceivingRecords.AnyAsync()).Should().BeFalse();
        (await _db.ScanActionLogs.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WithALot_StocksItUnderThatLot()
    {
        var (partId, lineId, binId) = await SeedAsync(contentPerUnit: null);

        await _handler.Handle(new ExecuteScanReceiveCommand(
            new ScanReceiveRequestModel(partId, lineId, 3m, binId, " HEAT-B ")),
            CancellationToken.None);

        _db.ChangeTracker.Clear();
        (await _db.ReceivingRecords.SingleAsync()).LotNumber.Should().Be("HEAT-B");
        (await _db.BinContents.SingleAsync(b => b.EntityId == partId && b.LotNumber == "HEAT-B")).Quantity.Should().Be(3m);
        (await _db.BinMovements.SingleAsync(m => m.EntityId == partId)).LotNumber.Should().Be("HEAT-B");
    }

    [Fact]
    public async Task Handle_LotTrackedPartWithoutALot_GetsAHouseLot()
    {
        var (partId, lineId, binId) = await SeedAsync(contentPerUnit: null, traceability: TraceabilityType.Lot);

        await HandlerWith(new StubCapabilitySnapshotProvider(ReceiptLotRecords.Capability)).Handle(
            new ExecuteScanReceiveCommand(new ScanReceiveRequestModel(partId, lineId, 3m, binId)),
            CancellationToken.None);

        _db.ChangeTracker.Clear();
        var lot = await _db.LotRecords.SingleAsync();
        (lot.LotNumber, lot.PartId, lot.Quantity).Should().Be(("LOT-20261007-001", partId, 3m));
        (await _db.BinContents.SingleAsync(b => b.EntityId == partId && b.LotNumber == "LOT-20261007-001")).Quantity.Should().Be(3m);
    }

    [Fact]
    public async Task Handle_LotTrackedPartWithALot_RecordsTheLot()
    {
        var (partId, lineId, binId) = await SeedAsync(contentPerUnit: null, traceability: TraceabilityType.Lot);

        await HandlerWith(new StubCapabilitySnapshotProvider(ReceiptLotRecords.Capability)).Handle(
            new ExecuteScanReceiveCommand(new ScanReceiveRequestModel(partId, lineId, 3m, binId, "HEAT-C")),
            CancellationToken.None);

        _db.ChangeTracker.Clear();
        var lot = await _db.LotRecords.SingleAsync();
        (lot.LotNumber, lot.PartId, lot.Quantity).Should().Be(("HEAT-C", partId, 3m));
    }

    public void Dispose() => _db.Dispose();
}
