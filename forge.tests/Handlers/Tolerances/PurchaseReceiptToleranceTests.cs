using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Features.Inventory;
using Forge.Api.Features.PurchaseOrders;
using Forge.Api.Features.Tolerances;
using Forge.Api.Features.VendorBills;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Core.Settings;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Tolerances;

public class PurchaseReceiptToleranceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly IClock _clock = TestCaller.ClockAt(Now);

    private ReceiveItemsHandler Handler(params string[] roles)
        => new(new PurchaseOrderRepository(_db), _clock, new Mock<MediatR.IMediator>().Object, TestCaller.As(1, roles), _db);

    private async Task<(PurchaseOrder Po, Part Part)> SeedAsync(
        decimal ordered = 100m, decimal? vendorOver = null, decimal? vendorUnder = null,
        decimal? lineOver = null, decimal? lineUnder = null, int lines = 1)
    {
        var vendor = new Vendor { CompanyName = "Molder", OverReceiptTolerancePct = vendorOver, UnderReceiptTolerancePct = vendorUnder };
        var part = new Part { PartNumber = "RESIN-1", Description = "Resin", Status = PartStatus.Active, IsMrpPlanned = true, LotSizingRule = LotSizingRule.LotForLot };
        _db.Vendors.Add(vendor);
        _db.Parts.Add(part);
        await _db.SaveChangesAsync();
        var po = new PurchaseOrder
        {
            PONumber = "PO-TOL-1", VendorId = vendor.Id, Status = PurchaseOrderStatus.Submitted,
            ExpectedDeliveryDate = Now.AddDays(10),
        };
        for (var i = 0; i < lines; i++)
            po.Lines.Add(new PurchaseOrderLine
            {
                PartId = part.Id, Description = "Resin", OrderedQuantity = ordered, UnitPrice = 2m,
                OverReceiptTolerancePct = lineOver, UnderReceiptTolerancePct = lineUnder,
            });
        _db.PurchaseOrders.Add(po);
        await _db.SaveChangesAsync();
        return (po, part);
    }

    private Task ReceiveAsync(PurchaseOrder po, decimal quantity, int lineIndex = 0, bool confirm = false, string? reason = null, string[]? roles = null)
        => Handler(roles ?? []).Handle(new ReceiveItemsCommand(po.Id,
            [new ReceiveLineModel(po.Lines.OrderBy(l => l.Id).ElementAt(lineIndex).Id, quantity, null, null,
                ConfirmOverTolerance: confirm, OverToleranceReason: reason)]), CancellationToken.None);

    private Task<PurchaseOrderLine> LineAsync(int id) => _db.PurchaseOrderLines.AsNoTracking().SingleAsync(l => l.Id == id);

    [Fact]
    public async Task NoTolerance_ShortReceipt_StaysOnBackOrder()
    {
        var (po, _) = await SeedAsync();

        await ReceiveAsync(po, 90m);

        var line = await LineAsync(po.Lines.First().Id);
        line.CancelledShortCloseQuantity.Should().Be(0m);
        line.RemainingQuantity.Should().Be(10m);
        (await _db.PurchaseOrders.AsNoTracking().SingleAsync()).Status.Should().Be(PurchaseOrderStatus.PartiallyReceived);
    }

    [Fact]
    public async Task NoTolerance_OverReceipt_IsRefused()
    {
        var (po, _) = await SeedAsync();

        var act = () => ReceiveAsync(po, 101m);

        (await act.Should().ThrowAsync<QuantityToleranceException>()).WithMessage("*only 100 remaining*");
        (await LineAsync(po.Lines.First().Id)).ReceivedQuantity.Should().Be(0m);
    }

    [Fact]
    public async Task UnderInsideVendorTolerance_ShortClosesTheRestWithinTolerance()
    {
        var (po, _) = await SeedAsync(vendorOver: 10m, vendorUnder: 10m);

        await ReceiveAsync(po, 90m);

        var line = await LineAsync(po.Lines.First().Id);
        line.CancelledShortCloseQuantity.Should().Be(10m);
        line.ShortCloseReason.Should().Be(PurchaseOrderLine.WithinToleranceReason);
        line.RemainingQuantity.Should().Be(0m);
        (await _db.PurchaseOrders.AsNoTracking().SingleAsync()).Status.Should().Be(PurchaseOrderStatus.Received);
        (await _db.ActivityLogs.AsNoTracking().AnyAsync(a => a.Action == "line-closed-within-tolerance")).Should().BeTrue();
    }

    [Fact]
    public async Task UnderBelowVendorTolerance_StaysOpen()
    {
        var (po, _) = await SeedAsync(vendorOver: 10m, vendorUnder: 10m);

        await ReceiveAsync(po, 89m);

        (await LineAsync(po.Lines.First().Id)).RemainingQuantity.Should().Be(11m);
    }

    [Fact]
    public async Task MoreArrivingAfterAToleranceClose_ShrinksTheShortClose()
    {
        var (po, _) = await SeedAsync(vendorOver: 10m, vendorUnder: 10m, lines: 2);
        await ReceiveAsync(po, 90m);

        await ReceiveAsync(po, 6m);

        var line = await LineAsync(po.Lines.OrderBy(l => l.Id).First().Id);
        line.ReceivedQuantity.Should().Be(96m);
        line.CancelledShortCloseQuantity.Should().Be(4m);
        (await _db.PurchaseOrders.AsNoTracking().SingleAsync()).Status.Should().Be(PurchaseOrderStatus.PartiallyReceived);
    }

    [Fact]
    public async Task OverInsideTolerance_IsAcceptedWithoutAnOverride()
    {
        var (po, _) = await SeedAsync(vendorOver: 10m, vendorUnder: 10m);

        await ReceiveAsync(po, 110m);

        var line = await LineAsync(po.Lines.First().Id);
        line.ReceivedQuantity.Should().Be(110m);
        line.CancelledShortCloseQuantity.Should().Be(0m);
        (await _db.ReceivingRecords.AsNoTracking().SingleAsync()).OverToleranceReason.Should().BeNull();
    }

    [Fact]
    public async Task OverAboveTolerance_NeedsAManagerToConfirmWithAReason()
    {
        var (po, _) = await SeedAsync(vendorOver: 10m, vendorUnder: 10m);

        var unconfirmed = () => ReceiveAsync(po, 140m, roles: ["Manager"]);
        (await unconfirmed.Should().ThrowAsync<QuantityToleranceException>()).Which.Violation.CanOverride.Should().BeTrue();
        var buyer = () => ReceiveAsync(po, 140m, confirm: true, reason: "Why 40 extra?", roles: ["Procurement"]);
        (await buyer.Should().ThrowAsync<QuantityToleranceException>()).Which.Violation.CanOverride.Should().BeFalse();

        await ReceiveAsync(po, 140m, confirm: true, reason: "Vendor shipped a full pallet", roles: ["Manager"]);

        (await LineAsync(po.Lines.First().Id)).ReceivedQuantity.Should().Be(140m);
        (await _db.ReceivingRecords.AsNoTracking().SingleAsync()).OverToleranceReason.Should().Be("Vendor shipped a full pallet");
        (await _db.ActivityLogs.AsNoTracking().AnyAsync(a => a.Action == "over-tolerance-accepted")).Should().BeTrue();
    }

    [Fact]
    public async Task LineOverride_WinsOverTheVendor()
    {
        var (po, _) = await SeedAsync(vendorOver: 10m, vendorUnder: 10m, lineOver: 0m, lineUnder: 0m);

        await ReceiveAsync(po, 95m);

        (await LineAsync(po.Lines.First().Id)).RemainingQuantity.Should().Be(5m);
    }

    [Fact]
    public async Task CompanyDefault_AppliesWhenNeitherLineNorVendorSetsOne()
    {
        _db.SystemSettings.AddRange(
            new SystemSetting { Key = ToleranceSettings.OverReceiptPercentKey, Value = "5" },
            new SystemSetting { Key = ToleranceSettings.UnderReceiptPercentKey, Value = "5" });
        await _db.SaveChangesAsync();
        var (po, _) = await SeedAsync();

        await ReceiveAsync(po, 95m);

        (await LineAsync(po.Lines.First().Id)).RemainingQuantity.Should().Be(0m);
    }

    [Fact]
    public async Task InventoryReceive_AppliesTheSameTolerance()
    {
        var (po, _) = await SeedAsync(vendorOver: 10m, vendorUnder: 10m);
        var bin = new StorageLocation { Name = "A1", LocationType = LocationType.Bin, IsActive = true };
        _db.StorageLocations.Add(bin);
        await _db.SaveChangesAsync();
        var handler = new ReceivePurchaseOrderHandler(
            new PurchaseOrderRepository(_db), new InventoryRepository(_db), TestCaller.As(1), _clock, _db);

        await handler.Handle(new ReceivePurchaseOrderCommand(
            new ReceivePurchaseOrderRequestModel(po.Lines.First().Id, 92m, bin.Id, null, null)), CancellationToken.None);

        var line = await LineAsync(po.Lines.First().Id);
        line.CancelledShortCloseQuantity.Should().Be(8m);
        line.ClosedWithinTolerance.Should().BeTrue();
    }

    [Fact]
    public async Task OverReceiptInsideTolerance_CanBeBilledUpToWhatWasReceived()
    {
        var (po, part) = await SeedAsync(vendorOver: 10m, vendorUnder: 10m);
        await ReceiveAsync(po, 105m);
        _db.Set<Currency>().Add(new Currency { Id = 1, Code = "USD", Name = "US Dollar", Symbol = "$" });
        var bill = new VendorBill
        {
            BillNumber = "BILL-TOL-1", VendorId = po.VendorId, PurchaseOrderId = po.Id, CurrencyId = 1, FxRate = 1m,
            BillDate = Now, DueDate = Now.AddDays(30),
            Lines =
            [
                new VendorBillLine
                {
                    PartId = part.Id, PurchaseOrderLineId = po.Lines.First().Id,
                    Description = "Resin", Quantity = 105m, UnitPrice = 2m, LineNumber = 1,
                },
            ],
        };
        _db.VendorBills.Add(bill);
        await _db.SaveChangesAsync();

        await new ApproveVendorBillHandler(new VendorBillRepository(_db), db: _db)
            .Handle(new ApproveVendorBillCommand(bill.Id), CancellationToken.None);

        (await LineAsync(po.Lines.First().Id)).BilledQuantity.Should().Be(105m);
    }

    [Fact]
    public async Task MrpOpenSupply_DropsTheQuantityClosedWithinTolerance()
    {
        var (po, part) = await SeedAsync(vendorOver: 10m, vendorUnder: 10m, lines: 2);
        await ReceiveAsync(po, 92m);

        var run = await new MrpService(_db, _clock, new PartSourcingResolver(_db), NullLogger<MrpService>.Instance)
            .ExecuteRunAsync(new MrpRunOptions());

        var poSupply = await _db.MrpSupplies
            .Where(s => s.MrpRunId == run.Id && s.PartId == part.Id && s.Source == MrpSupplySource.PurchaseOrder)
            .SumAsync(s => s.Quantity);
        poSupply.Should().Be(100m, "the second line is still fully open and the first is closed within tolerance");
    }
}
