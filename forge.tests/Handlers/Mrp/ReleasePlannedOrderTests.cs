using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Features.Mrp;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Mrp;

[Collection(PostgresCollection.Name)]
public sealed class ReleasePlannedOrderTests(PostgresFixture fixture)
{
    private const int NumberColumnLength = 20;

    private static ReleasePlannedOrderHandler Handler(AppDbContext db)
    {
        var clock = new SystemClock();
        return new ReleasePlannedOrderHandler(
            db,
            Mock.Of<IBarcodeService>(),
            new PurchaseOrderRepository(db),
            new JobRepository(db, clock),
            new BusinessIdentifierService(db, clock),
            new VendorCostResolver(db),
            Mock.Of<ICurrencyService>(c => c.GetBaseCurrencyAsync(It.IsAny<CancellationToken>()) == Task.FromResult("USD")));
    }

    private static MrpService Mrp(AppDbContext db, DateTimeOffset now)
    {
        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(now);
        return new MrpService(db, clock.Object, new PartSourcingResolver(db), NullLogger<MrpService>.Instance);
    }

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..16];

    private static async Task<(Vendor Vendor, Part Part)> SeedBoughtPartAsync(AppDbContext db)
    {
        var vendor = new Vendor { CompanyName = Unique("MRP-VEND") };
        db.Vendors.Add(vendor);
        await db.SaveChangesAsync();

        var part = new Part { PartNumber = Unique("MRP-BUY"), Description = "Bought bracket", PreferredVendorId = vendor.Id };
        db.Parts.Add(part);
        await db.SaveChangesAsync();
        return (vendor, part);
    }

    private static async Task<MrpPlannedOrder> SeedPlannedPurchaseAsync(AppDbContext db, int partId, decimal quantity)
    {
        var run = new MrpRun { RunNumber = Unique("MRP"), Status = MrpRunStatus.Completed, PlanningHorizonDays = 90 };
        db.MrpRuns.Add(run);
        await db.SaveChangesAsync();

        var order = new MrpPlannedOrder
        {
            MrpRunId = run.Id,
            PartId = partId,
            OrderType = MrpOrderType.Purchase,
            Status = MrpPlannedOrderStatus.Planned,
            Quantity = quantity,
            StartDate = DateTimeOffset.UtcNow,
            DueDate = DateTimeOffset.UtcNow.AddDays(20),
        };
        db.MrpPlannedOrders.Add(order);
        await db.SaveChangesAsync();
        return order;
    }

    private static async Task<VendorPart> SeedTierAsync(
        AppDbContext db, int vendorId, int partId, decimal unitPrice, bool isPreferred = true, string currency = "USD")
    {
        var vendorPart = new VendorPart { VendorId = vendorId, PartId = partId, IsPreferred = isPreferred, Currency = currency };
        db.VendorParts.Add(vendorPart);
        await db.SaveChangesAsync();
        db.VendorPartPriceTiers.Add(new VendorPartPriceTier
        {
            VendorPartId = vendorPart.Id,
            MinQuantity = 1,
            UnitPrice = unitPrice,
            Currency = currency,
            EffectiveFrom = DateTimeOffset.UtcNow.AddDays(-1),
        });
        await db.SaveChangesAsync();
        return vendorPart;
    }

    [Fact]
    public async Task ReleasePurchase_NumbersFromThePoSequence_RegistersTheNumber_AndPricesTheLine()
    {
        int plannedOrderId;
        string expectedNumber;
        await using (var seed = fixture.CreateContext())
        {
            var (vendor, part) = await SeedBoughtPartAsync(seed);
            await SeedTierAsync(seed, vendor.Id, part.Id, 2.50m);

            var lastNumber = Random.Shared.Next(60000, 89999);
            seed.PurchaseOrders.Add(new PurchaseOrder { PONumber = $"PO-{lastNumber:D5}", VendorId = vendor.Id });
            await seed.SaveChangesAsync();
            expectedNumber = $"PO-{lastNumber + 1:D5}";

            plannedOrderId = (await SeedPlannedPurchaseAsync(seed, part.Id, 9.2m)).Id;
        }

        await using var db = fixture.CreateContext();
        var result = await Handler(db).Handle(new ReleasePlannedOrderCommand(plannedOrderId), CancellationToken.None);

        await using var verify = fixture.CreateContext();
        var po = await verify.PurchaseOrders.Include(p => p.Lines).SingleAsync(p => p.Id == result.CreatedPurchaseOrderId);
        po.PONumber.Should().Be(expectedNumber);
        po.PONumber.Length.Should().BeLessThanOrEqualTo(NumberColumnLength);
        po.OriginSource.Should().Be(PoOriginSource.AutoMrp);

        var line = po.Lines.Should().ContainSingle().Subject;
        line.OrderedQuantity.Should().Be(10);
        line.UnitPrice.Should().Be(2.50m);
        line.MrpPlannedOrderId.Should().Be(plannedOrderId);

        (await verify.BusinessIdentifiers.AnyAsync(i => i.EntityType == BusinessEntityType.PurchaseOrder
            && i.EntityId == po.Id && i.Value == expectedNumber && i.IsActive)).Should().BeTrue();
        (await verify.ActivityLogs.AnyAsync(a => a.EntityType == "PurchaseOrder" && a.EntityId == po.Id && a.Action == "created"))
            .Should().BeTrue();
        (await verify.MrpPlannedOrders.SingleAsync(o => o.Id == plannedOrderId)).Status
            .Should().Be(MrpPlannedOrderStatus.Released);
    }

    private static async Task SeedOtherVendorTierAndSalesPriceAsync(AppDbContext db, int partId)
    {
        var otherVendor = new Vendor { CompanyName = Unique("MRP-OTHER") };
        db.Vendors.Add(otherVendor);
        await db.SaveChangesAsync();
        await SeedTierAsync(db, otherVendor.Id, partId, 1.10m);
        db.PartPrices.Add(new PartPrice { PartId = partId, UnitPrice = 4.75m, EffectiveFrom = DateTimeOffset.UtcNow.AddDays(-1) });
        await db.SaveChangesAsync();
    }

    private async Task<PurchaseOrder> ReleaseAndLoadPoAsync(int plannedOrderId)
    {
        await using var db = fixture.CreateContext();
        var result = await Handler(db).Handle(new ReleasePlannedOrderCommand(plannedOrderId), CancellationToken.None);
        await using var verify = fixture.CreateContext();
        return await verify.PurchaseOrders.Include(p => p.Lines).SingleAsync(p => p.Id == result.CreatedPurchaseOrderId);
    }

    [Fact]
    public async Task ReleasePurchase_UsesThePoVendorsOwnTier_EvenWhenAnotherVendorIsPreferred()
    {
        int plannedOrderId;
        await using (var seed = fixture.CreateContext())
        {
            var (vendor, part) = await SeedBoughtPartAsync(seed);
            await SeedOtherVendorTierAndSalesPriceAsync(seed, part.Id);
            await SeedTierAsync(seed, vendor.Id, part.Id, 3.20m, isPreferred: false, currency: "CAD");
            plannedOrderId = (await SeedPlannedPurchaseAsync(seed, part.Id, 5)).Id;
        }

        var po = await ReleaseAndLoadPoAsync(plannedOrderId);

        po.QuoteCurrency.Should().Be("CAD");
        po.Lines.Should().ContainSingle().Which.UnitPrice.Should().Be(3.20m);
    }

    [Fact]
    public async Task ReleasePurchase_WithoutATierFromThePoVendor_UsesTheManualCost_NotTheSalesPriceOrAnotherVendorsTier()
    {
        int plannedOrderId;
        await using (var seed = fixture.CreateContext())
        {
            var (_, part) = await SeedBoughtPartAsync(seed);
            await SeedOtherVendorTierAndSalesPriceAsync(seed, part.Id);
            part.ManualCostOverride = 3.05m;
            await seed.SaveChangesAsync();
            plannedOrderId = (await SeedPlannedPurchaseAsync(seed, part.Id, 5)).Id;
        }

        var po = await ReleaseAndLoadPoAsync(plannedOrderId);

        po.QuoteCurrency.Should().Be("USD");
        po.Lines.Should().ContainSingle().Which.UnitPrice.Should().Be(3.05m);
    }

    [Fact]
    public async Task ReleasePurchase_WithNoCostForThePoVendor_PricesTheLineAtZero_AndNeverAtTheSalesPrice()
    {
        int plannedOrderId;
        await using (var seed = fixture.CreateContext())
        {
            var (_, part) = await SeedBoughtPartAsync(seed);
            await SeedOtherVendorTierAndSalesPriceAsync(seed, part.Id);
            plannedOrderId = (await SeedPlannedPurchaseAsync(seed, part.Id, 3)).Id;
        }

        var po = await ReleaseAndLoadPoAsync(plannedOrderId);

        po.PONumber.Length.Should().BeLessThanOrEqualTo(NumberColumnLength);
        po.Lines.Should().ContainSingle().Which.UnitPrice.Should().Be(0m);
    }

    [Fact]
    public async Task ReleaseToJob_GivesTheJobANumberAndAQuantity_AndASecondMrpRunPlansOnlyItsComponents()
    {
        int parentPartId;
        int childPartId;
        await using (var seed = fixture.CreateContext())
        {
            var track = new TrackType { Name = "MRP-PG Track", Code = Unique("mrp-pg"), IsActive = true };
            seed.TrackTypes.Add(track);
            await seed.SaveChangesAsync();
            seed.JobStages.Add(new JobStage { TrackTypeId = track.Id, Name = "Stage 1", Code = "s1", SortOrder = 1, IsActive = true });

            var parent = new Part
            {
                PartNumber = Unique("MRP-MAKE"),
                Description = "Made assembly",
                Status = PartStatus.Active,
                IsMrpPlanned = true,
                LotSizingRule = LotSizingRule.LotForLot,
            };
            var child = new Part
            {
                PartNumber = Unique("MRP-COMP"),
                Description = "Component",
                Status = PartStatus.Active,
                IsMrpPlanned = true,
                LotSizingRule = LotSizingRule.LotForLot,
            };
            seed.Parts.AddRange(parent, child);
            await seed.SaveChangesAsync();

            seed.BOMLines.Add(new BOMLine
            {
                ParentPartId = parent.Id,
                ChildPartId = child.Id,
                Quantity = 2,
                SourceType = BOMSourceType.Buy,
                SortOrder = 1,
            });

            var schedule = new MasterSchedule
            {
                Name = Unique("MPS"),
                Status = MasterScheduleStatus.Active,
                PeriodStart = DateTimeOffset.UtcNow,
                PeriodEnd = DateTimeOffset.UtcNow.AddDays(90),
            };
            schedule.Lines.Add(new MasterScheduleLine { PartId = parent.Id, Quantity = 25, DueDate = DateTimeOffset.UtcNow.AddDays(60) });
            seed.MasterSchedules.Add(schedule);
            await seed.SaveChangesAsync();
            parentPartId = parent.Id;
            childPartId = child.Id;
        }

        var options = new MrpRunOptions(PartIds: [parentPartId, childPartId]);
        var firstRunAt = DateTimeOffset.UtcNow;

        MrpPlannedOrder build;
        await using (var db = fixture.CreateContext())
        {
            var firstRun = await Mrp(db, firstRunAt).ExecuteRunAsync(options);
            build = await db.MrpPlannedOrders.AsNoTracking()
                .SingleAsync(o => o.MrpRunId == firstRun.Id && o.PartId == parentPartId);
            build.OrderType.Should().Be(MrpOrderType.Manufacture);
        }

        ReleasePlannedOrderResult result;
        await using (var db = fixture.CreateContext())
            result = await Handler(db).Handle(new ReleasePlannedOrderCommand(build.Id), CancellationToken.None);

        await using (var verify = fixture.CreateContext())
        {
            var job = await verify.Jobs.Include(j => j.JobParts).Include(j => j.ActivityLogs)
                .SingleAsync(j => j.Id == result.CreatedJobId);
            job.JobNumber.Should().StartWith("J-");
            job.JobNumber.Length.Should().BeLessThanOrEqualTo(NumberColumnLength);
            job.StartDate.Should().Be(build.StartDate);
            job.DueDate.Should().Be(build.DueDate);
            var jobPart = job.JobParts.Should().ContainSingle().Subject;
            jobPart.PartId.Should().Be(parentPartId);
            jobPart.Quantity.Should().Be(25);
            job.ActivityLogs.Should().ContainSingle(l => l.Action == ActivityAction.Created);
            (await verify.BusinessIdentifiers.AnyAsync(i => i.EntityType == BusinessEntityType.Job
                && i.EntityId == job.Id && i.Value == job.JobNumber && i.IsActive)).Should().BeTrue();
        }

        await using (var db = fixture.CreateContext())
        {
            var secondRun = await Mrp(db, firstRunAt.AddMinutes(1)).ExecuteRunAsync(options);
            (await db.MrpPlannedOrders.AnyAsync(o => o.MrpRunId == secondRun.Id && o.PartId == parentPartId)).Should().BeFalse();
            var componentOrder = await db.MrpPlannedOrders.SingleAsync(o => o.MrpRunId == secondRun.Id && o.PartId == childPartId);
            componentOrder.OrderType.Should().Be(MrpOrderType.Purchase);
            componentOrder.Quantity.Should().Be(50);
            (await db.MrpSupplies.SingleAsync(s => s.MrpRunId == secondRun.Id && s.Source == MrpSupplySource.Job))
                .SourceEntityId.Should().Be(result.CreatedJobId);
        }
    }
}
