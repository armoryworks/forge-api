using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
using Forge.Api.Features.Inventory;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Inventory;

public class ReconcileConsignmentHandlerTests
{
    private static ConsignmentStockService Service(AppDbContext db, params string[] capabilities) => new(
        db, new InventoryRepository(db), new VendorBillRepository(db), InventoryLedgerTestBook.Engine(db),
        new InventoryValuationService(db), new StubCapabilitySnapshotProvider(capabilities), new SystemClock());

    private static async Task<(ConsignmentAgreement Agreement, int LocationId)> SeedAsync(AppDbContext db)
    {
        var part = new Part { PartNumber = "P-300", Name = "Fastener", InventoryClass = InventoryClass.Raw };
        var vendor = new Vendor { CompanyName = "Fastener Supply" };
        var location = new StorageLocation { Name = "Rack A", LocationType = LocationType.Bin, IsActive = true };
        db.AddRange(part, vendor, location);
        await db.SaveChangesAsync();
        var agreement = new ConsignmentAgreement
        {
            Direction = ConsignmentDirection.Inbound, VendorId = vendor.Id, PartId = part.Id, AgreedUnitPrice = 0.5m,
            EffectiveFrom = new DateOnly(2026, 1, 1),
        };
        db.ConsignmentAgreements.Add(agreement);
        await db.SaveChangesAsync();
        return (agreement, location.Id);
    }

    [Fact]
    public async Task Reconcile_TwiceAtTheSameCount_PostsTheVarianceOnce()
    {
        using var db = TestDbContextFactory.Create();
        var (agreement, locationId) = await SeedAsync(db);
        var service = Service(db);
        await service.ReceiveAsync(agreement.Id, new RecordConsignmentTransactionRequestModel { Quantity = 2000m, StorageLocationId = locationId }, 1);
        await service.TakeOwnershipAsync(agreement.Id, 350m, null, null, null, null, 1);
        var handler = new ReconcileConsignmentHandler(service);
        var command = new ReconcileConsignmentCommand(agreement.Id, new ReconcileConsignmentRequestModel { PhysicalCount = 1600m, CreateBill = false }, 1);

        var first = await handler.Handle(command, CancellationToken.None);
        var second = await handler.Handle(command, CancellationToken.None);

        first.BookQuantity.Should().Be(1650m);
        first.Variance.Should().Be(-50m);
        second.BookQuantity.Should().Be(1600m);
        second.Variance.Should().Be(0m);
        second.AdjustmentTransactionId.Should().BeNull();
        db.ConsignmentTransactions.Count(t => t.Type == ConsignmentTransactionType.Reconciliation).Should().Be(1);
        (await db.BinContents.Where(c => c.ConsignmentAgreementId == agreement.Id && c.RemovedAt == null).SumAsync(c => c.Quantity))
            .Should().Be(1600m);
    }

    [Fact]
    public async Task Reconcile_DraftsOneBillForTheUnbilledConsumptionAndNotAgain()
    {
        using var db = TestDbContextFactory.Create();
        var (agreement, locationId) = await SeedAsync(db);
        var service = Service(db, "CAP-P2P-BILL");
        await service.ReceiveAsync(agreement.Id, new RecordConsignmentTransactionRequestModel { Quantity = 100m, StorageLocationId = locationId }, 1);
        await service.TakeOwnershipAsync(agreement.Id, 30m, null, null, null, null, 1);
        await service.TakeOwnershipAsync(agreement.Id, 10m, null, null, null, null, 1);

        var first = await service.ReconcileAsync(agreement.Id, new ReconcileConsignmentRequestModel(), 1);
        var second = await service.ReconcileAsync(agreement.Id, new ReconcileConsignmentRequestModel(), 1);

        first.ConsumedQuantity.Should().Be(40m);
        first.ConsumedAmount.Should().Be(20m);
        first.VendorBillId.Should().NotBeNull();
        var bill = await db.VendorBills.Include(b => b.Lines).SingleAsync();
        bill.Status.Should().Be(VendorBillStatus.Draft);
        bill.VendorId.Should().Be(agreement.VendorId!.Value);
        bill.Lines.Should().HaveCount(2).And.OnlyContain(l => l.AccountDeterminationKey == "CONSIGNMENT_LIABILITY");
        bill.Lines.Sum(l => l.LineTotal).Should().Be(20m);
        second.VendorBillId.Should().BeNull();
        second.ConsumedQuantity.Should().Be(0m);
    }

    [Fact]
    public async Task Reconcile_WithBillsOff_ReturnsTheStatementWithoutABill()
    {
        using var db = TestDbContextFactory.Create();
        var (agreement, locationId) = await SeedAsync(db);
        var service = Service(db);
        await service.ReceiveAsync(agreement.Id, new RecordConsignmentTransactionRequestModel { Quantity = 10m, StorageLocationId = locationId }, 1);
        await service.TakeOwnershipAsync(agreement.Id, 4m, null, null, null, null, 1);

        var result = await service.ReconcileAsync(agreement.Id, new ReconcileConsignmentRequestModel(), 1);

        result.BillingAvailable.Should().BeFalse();
        result.ConsumedQuantity.Should().Be(4m);
        result.VendorBillId.Should().BeNull();
        (await db.VendorBills.CountAsync()).Should().Be(0);
    }
}
