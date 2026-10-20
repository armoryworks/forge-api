using System.Security.Claims;

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.Inventory;
using Forge.Api.Features.Lots;
using Forge.Api.Features.PurchaseOrders;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Lots;

public class ReceiptLotAssignmentTests : IDisposable
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
    }

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly FixedClock _clock = new();
    private readonly Mock<IHttpContextAccessor> _http = new();

    public ReceiptLotAssignmentTests()
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "7"), new Claim(ClaimTypes.Name, "Receiver")], "Test");
        _http.Setup(a => a.HttpContext).Returns(new DefaultHttpContext { User = new ClaimsPrincipal(identity) });
    }

    private async Task<(PurchaseOrder Po, List<PurchaseOrderLine> Lines, StorageLocation Bin)> SeedAsync(
        TraceabilityType traceability = TraceabilityType.Lot, int lineCount = 1)
    {
        var vendor = new Vendor { CompanyName = "Fastener supply" };
        var bin = new StorageLocation { Name = "RC-1", LocationType = LocationType.Bin, IsActive = true };
        var parts = Enumerable.Range(1, lineCount)
            .Select(i => new Part { PartNumber = $"HW-{i}", Description = "Insert", TraceabilityType = traceability })
            .ToList();
        _db.AddRange(vendor, bin);
        _db.Parts.AddRange(parts);
        await _db.SaveChangesAsync();

        var po = new PurchaseOrder { PONumber = "PO-LOT-1", VendorId = vendor.Id, Status = PurchaseOrderStatus.Submitted };
        _db.PurchaseOrders.Add(po);
        await _db.SaveChangesAsync();
        var lines = parts
            .Select(p => new PurchaseOrderLine { PurchaseOrderId = po.Id, PartId = p.Id, OrderedQuantity = 100m, UnitPrice = 1m })
            .ToList();
        _db.PurchaseOrderLines.AddRange(lines);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return (po, lines, bin);
    }

    private Task ReceiveItemsAsync(int poId, IEnumerable<ReceiveLineModel> lines, bool lotsOn = true)
        => new ReceiveItemsHandler(
                new PurchaseOrderRepository(_db), _clock, Mock.Of<MediatR.IMediator>(), _http.Object,
                _db, null, new InventoryRepository(_db),
                lotsOn ? new StubCapabilitySnapshotProvider(ReceiptLotRecords.Capability) : new StubCapabilitySnapshotProvider(),
                Mock.Of<IBarcodeService>())
            .Handle(new ReceiveItemsCommand(poId, lines.ToList()), CancellationToken.None);

    private Task<ReceivingRecordResponseModel> ReceivePurchaseOrderAsync(int lineId, int binId, string? lot)
        => new ReceivePurchaseOrderHandler(
                new PurchaseOrderRepository(_db), new InventoryRepository(_db), _http.Object, _clock,
                _db, null, new StubCapabilitySnapshotProvider(ReceiptLotRecords.Capability), Mock.Of<IBarcodeService>())
            .Handle(new ReceivePurchaseOrderCommand(
                new ReceivePurchaseOrderRequestModel(lineId, 5m, binId, lot, null)), CancellationToken.None);

    [Fact]
    public async Task ReceiveItems_LotTrackedPartWithoutALot_GetsTheNextHouseLot()
    {
        var (po, lines, bin) = await SeedAsync(lineCount: 2);
        _db.LotRecords.Add(new LotRecord { LotNumber = "LOT-20261007-004", PartId = lines[0].PartId!.Value, Quantity = 1m, DeletedAt = _clock.UtcNow });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        await ReceiveItemsAsync(po.Id, lines.Select(l => new ReceiveLineModel(l.Id, 5m, bin.Id, null)));

        var records = await _db.ReceivingRecords.AsNoTracking().OrderBy(r => r.PurchaseOrderLineId).ToListAsync();
        records.Select(r => r.LotNumber).Should().Equal("LOT-20261007-005", "LOT-20261007-006");
        var lots = await _db.LotRecords.AsNoTracking().Where(l => l.DeletedAt == null).OrderBy(l => l.LotNumber).ToListAsync();
        lots.Select(l => (l.LotNumber, l.PartId, l.Quantity)).Should().Equal(
            ("LOT-20261007-005", lines[0].PartId!.Value, 5m), ("LOT-20261007-006", lines[1].PartId!.Value, 5m));
        (await _db.BinContents.AsNoTracking().Where(b => b.EntityId == lines[0].PartId).SingleAsync())
            .LotNumber.Should().Be("LOT-20261007-005");
    }

    [Fact]
    public async Task ReceiveItems_GivenLot_IsKept()
    {
        var (po, lines, bin) = await SeedAsync();

        await ReceiveItemsAsync(po.Id, [new ReceiveLineModel(lines[0].Id, 5m, bin.Id, null, LotNumber: "HEAT-7")]);

        (await _db.ReceivingRecords.AsNoTracking().SingleAsync()).LotNumber.Should().Be("HEAT-7");
    }

    [Theory]
    [InlineData(TraceabilityType.Lot, false)]
    [InlineData(TraceabilityType.None, true)]
    public async Task ReceiveItems_LotsOffOrUntrackedPart_StaysUnlotted(TraceabilityType traceability, bool lotsOn)
    {
        var (po, lines, bin) = await SeedAsync(traceability);

        await ReceiveItemsAsync(po.Id, [new ReceiveLineModel(lines[0].Id, 5m, bin.Id, null)], lotsOn);

        (await _db.ReceivingRecords.AsNoTracking().SingleAsync()).LotNumber.Should().BeNull();
        (await _db.LotRecords.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task ReceivePurchaseOrder_LotTrackedPartWithoutALot_GetsAHouseLot()
    {
        var (_, lines, bin) = await SeedAsync();

        var first = await ReceivePurchaseOrderAsync(lines[0].Id, bin.Id, "  ");
        _db.ChangeTracker.Clear();
        var second = await ReceivePurchaseOrderAsync(lines[0].Id, bin.Id, null);

        (first.LotNumber, second.LotNumber).Should().Be(("LOT-20261007-001", "LOT-20261007-002"));
        (await _db.LotRecords.CountAsync()).Should().Be(2);
    }

    public void Dispose() => _db.Dispose();
}
