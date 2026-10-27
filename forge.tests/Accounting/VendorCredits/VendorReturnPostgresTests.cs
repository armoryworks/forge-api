using FluentAssertions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Quality;
using Forge.Api.Features.VendorReturns;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Integrations;
using Forge.Tests.Helpers;

using static Forge.Tests.Accounting.VendorCredits.VendorMemoPgContext;

namespace Forge.Tests.Accounting.VendorCredits;

/// <summary>
/// NCR Return To Vendor against real Postgres with the built-in ledger on: the disposition opens a return for the held
/// lot; the NCR can't close until it ships; shipping takes the lot out of QcHold, out of the valuation store and from
/// inventory into the vendor returns clearing account at the receipt cost; the vendor's credit clears that account.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class VendorReturnPostgresTests(PostgresFixture fixture)
{
    private readonly VendorMemoPgContext ctx = new(fixture);

    private async Task<(int NcrId, int PartId, int VendorId)> SeedHeldLotAsync()
    {
        await ctx.ResetAsync();
        var vendorId = await ctx.VendorAsync("Resin Co");
        await using var db = ctx.Db();
        var part = new Part { PartNumber = "RES-1", Name = "Resin", InventoryClass = InventoryClass.Raw };
        var location = new StorageLocation { Name = "QC cage", Barcode = "QC1", LocationType = LocationType.Bin, IsActive = true };
        db.AddRange(part, location);
        await db.SaveChangesAsync();

        var po = new PurchaseOrder
        {
            PONumber = "PO-1", VendorId = vendorId, Status = PurchaseOrderStatus.Received, CurrencyId = UsdId,
            Lines = [new PurchaseOrderLine { PartId = part.Id, Description = "Resin", OrderedQuantity = 20m, ReceivedQuantity = 20m, UnitPrice = 10m }],
        };
        db.PurchaseOrders.Add(po);
        await db.SaveChangesAsync();
        var poLine = po.Lines.Single();
        db.Set<ReceivingRecord>().Add(new ReceivingRecord { PurchaseOrderLineId = poLine.Id, QuantityReceived = 20m, LotNumber = "L-1", FxRate = 1m });
        db.LotRecords.Add(new LotRecord { LotNumber = "L-1", PartId = part.Id, PurchaseOrderLineId = poLine.Id, Quantity = 20m });
        db.BinContents.Add(new BinContent
        {
            LocationId = location.Id, EntityType = "part", EntityId = part.Id, Quantity = 20m, LotNumber = "L-1",
            Status = BinContentStatus.QcHold, PlacedBy = ctx.UserId, PlacedAt = March,
        });
        db.InventoryValuations.Add(new InventoryValuation { BookId = BookId, PartId = part.Id, OnHandQuantity = 20m, TotalValue = 200m, AverageUnitCost = 10m });
        var ncr = new NonConformance
        {
            NcrNumber = "NCR-1", Type = NcrType.Supplier, PartId = part.Id, LotNumber = "L-1", DetectedById = ctx.UserId,
            DetectedAt = March, DetectedAtStage = NcrDetectionStage.Receiving, Description = "Contaminated",
            AffectedQuantity = 20m, DefectiveQuantity = 20m, Status = NcrStatus.Open, PurchaseOrderLineId = poLine.Id,
        };
        db.NonConformances.Add(ncr);
        await db.SaveChangesAsync();
        return (ncr.Id, part.Id, vendorId);
    }

    [Fact]
    public async Task Ncr_return_to_vendor_ships_the_held_lot_and_the_vendor_credit_clears_the_clearing_account()
    {
        var (ncrId, partId, vendorId) = await SeedHeldLotAsync();

        await using (var db = ctx.Db())
        {
            await new DispositionNcrHandler(db, new SystemClock(), ctx.Http(), capabilities: Caps())
                .Handle(new DispositionNcrCommand(ncrId, new DispositionNcrRequestModel { Code = NcrDispositionCode.ReturnToVendor }), CancellationToken.None);
        }

        int returnId;
        await using (var db = ctx.Db())
        {
            var rtv = await db.VendorReturns.Include(r => r.Lines).SingleAsync();
            rtv.Status.Should().Be(VendorReturnStatus.Open);
            rtv.VendorId.Should().Be(vendorId);
            rtv.Lines.Single().Should().Match<VendorReturnLine>(l => l.Quantity == 20m && l.UnitCost == 10m && l.UnitPrice == 10m);
            returnId = rtv.Id;

            var ncr = await new GetNcrByIdHandler(db).Handle(new GetNcrByIdQuery(ncrId), CancellationToken.None);
            ncr.VendorReturnNumber.Should().Be(rtv.ReturnNumber);
            ncr.VendorReturnStatus.Should().Be(VendorReturnStatus.Open);
        }

        await using (var db = ctx.Db())
        {
            (await FluentActions.Awaiting(() => new CloseNcrHandler(db).Handle(new CloseNcrCommand(ncrId, new CloseNcrRequestModel()), CancellationToken.None))
                .Should().ThrowAsync<ValidationException>())
                .Which.Errors.Should().Contain(e => e.ErrorCode == CloseNcrHandler.VendorReturnOpenCode);
        }

        await using (var db = ctx.Db())
        {
            var shipped = await new ShipVendorReturnHandler(db, MemoPosting(db), new SystemClock(), ctx.Mediator(db).Object)
                .Handle(new ShipVendorReturnCommand(returnId, new ShipVendorReturnRequestModel(March.AddDays(1), "UPS", "1Z999", "RMA-5")), CancellationToken.None);
            shipped.Status.Should().Be("Shipped");
            shipped.Lines.Single().ShippedCost.Should().Be(200m);
        }

        await using (var db = ctx.Db())
        {
            (await db.BinContents.Where(b => b.LotNumber == "L-1" && b.RemovedAt == null).SumAsync(b => b.Quantity)).Should().Be(0m);
            (await db.BinMovements.SingleAsync(m => m.LotNumber == "L-1")).Reason.Should().Be(BinMovementReason.Return);
            var valuation = await db.InventoryValuations.SingleAsync(v => v.PartId == partId);
            valuation.OnHandQuantity.Should().Be(0m);
            valuation.TotalValue.Should().Be(0m);
        }
        var net = await ctx.NetByAccountAsync();
        net[ClearingId].Should().Be(200m);
        net[InventoryRawId].Should().Be(-200m);

        await using (var db = ctx.Db())
        {
            await new CloseNcrHandler(db).Handle(new CloseNcrCommand(ncrId, new CloseNcrRequestModel()), CancellationToken.None);
        }

        await using (var db = ctx.Db())
        {
            var memo = await new RecordVendorReturnCreditHandler(db, ctx.Mediator(db).Object)
                .Handle(new RecordVendorReturnCreditCommand(returnId, new RecordVendorReturnCreditRequestModel("VC-RTV-1")), CancellationToken.None);
            memo.Status.Should().Be("Open");
            memo.Total.Should().Be(200m);
            memo.VendorReturnId.Should().Be(returnId);
        }

        net = await ctx.NetByAccountAsync();
        net[ClearingId].Should().Be(0m);
        net[ApId].Should().Be(200m);
        (await ctx.SignedApOpenItemsAsync()).Should().Be(-200m);
        await using (var db = ctx.Db())
        {
            (await db.VendorReturns.SingleAsync()).Status.Should().Be(VendorReturnStatus.Closed);
            (await db.ApOpenItems.SingleAsync()).Sign.Should().Be((short)-1);
        }
    }

    [Fact]
    public async Task A_credit_below_the_shipped_value_settles_the_clearing_account_through_price_variance()
    {
        var (ncrId, _, _) = await SeedHeldLotAsync();
        await using (var db = ctx.Db())
        {
            await new DispositionNcrHandler(db, new SystemClock(), ctx.Http(), capabilities: Caps())
                .Handle(new DispositionNcrCommand(ncrId, new DispositionNcrRequestModel { Code = NcrDispositionCode.ReturnToVendor }), CancellationToken.None);
        }
        int returnId, lineId;
        await using (var db = ctx.Db())
        {
            var rtv = await db.VendorReturns.Include(r => r.Lines).SingleAsync();
            returnId = rtv.Id;
            lineId = rtv.Lines.Single().Id;
            await new ShipVendorReturnHandler(db, MemoPosting(db), new SystemClock(), ctx.Mediator(db).Object)
                .Handle(new ShipVendorReturnCommand(returnId, new ShipVendorReturnRequestModel()), CancellationToken.None);
        }
        await using (var db = ctx.Db())
        {
            await new RecordVendorReturnCreditHandler(db, ctx.Mediator(db).Object).Handle(
                new RecordVendorReturnCreditCommand(returnId, new RecordVendorReturnCreditRequestModel(
                    Lines: [new VendorReturnCreditLineRequestModel(lineId, UnitPrice: 9m)])), CancellationToken.None);
        }

        var net = await ctx.NetByAccountAsync();
        net[ClearingId].Should().Be(0m);
        net[PpvId].Should().Be(20m);
        net[ApId].Should().Be(180m);
    }
}
