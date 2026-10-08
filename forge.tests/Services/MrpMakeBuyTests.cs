using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Services;

public class MrpMakeBuyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
    private const int NineStepMakeDaysAt500 = 6;

    private static IClock Clock()
    {
        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(Now);
        return clock.Object;
    }

    private static MrpService Mrp(AppDbContext db) =>
        new(db, Clock(), new PartSourcingResolver(db), NullLogger<MrpService>.Instance);

    private static async Task<Part> SeedPartAsync(AppDbContext db, string number, ProcurementSource source)
    {
        var part = new Part
        {
            PartNumber = number,
            Name = number,
            Description = number,
            Status = PartStatus.Active,
            ProcurementSource = source,
            InventoryClass = InventoryClass.Component,
            IsMrpPlanned = true,
            LotSizingRule = LotSizingRule.LotForLot,
        };
        db.Parts.Add(part);
        await db.SaveChangesAsync();
        return part;
    }

    private static async Task SeedNineStepRoutingAsync(AppDbContext db, int partId)
    {
        for (var step = 1; step <= 9; step++)
        {
            db.Operations.Add(new Operation
            {
                PartId = partId,
                StepNumber = step * 10,
                Title = $"Step {step}",
                SetupMinutes = 30m,
                RunMinutesEach = 0.5m,
            });
        }
        await db.SaveChangesAsync();
    }

    private static async Task SeedPreferredVendorAsync(AppDbContext db, int partId, int leadTimeDays)
    {
        var vendor = new Vendor { CompanyName = $"Vendor {partId}" };
        db.Vendors.Add(vendor);
        await db.SaveChangesAsync();
        db.VendorParts.Add(new VendorPart
        {
            VendorId = vendor.Id,
            PartId = partId,
            IsPreferred = true,
            LeadTimeDays = leadTimeDays,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<SalesOrderLine> SeedSoLineAsync(AppDbContext db, int partId, int quantity, int daysOut)
    {
        var customer = new Customer { Name = $"Customer {partId}" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var so = new SalesOrder
        {
            OrderNumber = $"SO-MB-{partId}",
            CustomerId = customer.Id,
            Status = SalesOrderStatus.Confirmed,
            RequestedDeliveryDate = Now.AddDays(daysOut),
        };
        db.SalesOrders.Add(so);
        await db.SaveChangesAsync();

        var line = new SalesOrderLine
        {
            SalesOrderId = so.Id,
            PartId = partId,
            Description = "Line",
            Quantity = quantity,
            UnitPrice = 10m,
            LineNumber = 1,
        };
        db.SalesOrderLines.Add(line);
        await db.SaveChangesAsync();
        return line;
    }

    private static async Task<MrpPlannedOrder> PlannedOrderAsync(AppDbContext db, MrpRunResponseModel run, int partId) =>
        await db.MrpPlannedOrders.SingleAsync(o => o.MrpRunId == run.Id && o.PartId == partId);

    [Fact]
    public void NineStepRouting_AtQuantity500_TakesSixShopDays()
    {
        var routing = Enumerable.Range(1, 9)
            .Select(step => new Operation { StepNumber = step, SetupMinutes = 30m, RunMinutesEach = 0.5m })
            .ToList();

        OperationTimeMath.MakeLeadTimeDays(routing, 500m).Should().Be(NineStepMakeDaysAt500);
    }

    [Fact]
    public async Task MakePart_WithoutBom_PlansManufacture()
    {
        using var db = TestDbContextFactory.Create();
        var part = await SeedPartAsync(db, "MB-MAKE-NOBOM", ProcurementSource.Make);
        await SeedSoLineAsync(db, part.Id, 20, daysOut: 60);

        var run = await Mrp(db).ExecuteRunAsync(new MrpRunOptions(PartIds: [part.Id]));

        var order = await PlannedOrderAsync(db, run, part.Id);
        order.OrderType.Should().Be(MrpOrderType.Manufacture);
        order.StartDate.Should().Be(order.DueDate.AddDays(-14));
    }

    [Fact]
    public async Task BuyPart_WithBomAndVendor_PlansPurchase_OnVendorLeadTime_WithoutExplodingTheBom()
    {
        using var db = TestDbContextFactory.Create();
        var part = await SeedPartAsync(db, "MB-BUY-BOM", ProcurementSource.Buy);
        var component = await SeedPartAsync(db, "MB-BUY-COMP", ProcurementSource.Buy);
        db.BOMLines.Add(new BOMLine { ParentPartId = part.Id, ChildPartId = component.Id, Quantity = 2, SortOrder = 1 });
        await db.SaveChangesAsync();
        await SeedNineStepRoutingAsync(db, part.Id);
        await SeedPreferredVendorAsync(db, part.Id, leadTimeDays: 10);
        await SeedSoLineAsync(db, part.Id, 20, daysOut: 60);

        var run = await Mrp(db).ExecuteRunAsync(new MrpRunOptions(PartIds: [part.Id, component.Id]));

        var order = await PlannedOrderAsync(db, run, part.Id);
        order.OrderType.Should().Be(MrpOrderType.Purchase);
        order.StartDate.Should().Be(order.DueDate.AddDays(-10));
        (await db.MrpPlannedOrders.AnyAsync(o => o.MrpRunId == run.Id && o.PartId == component.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task BuyPart_WithRoutingAndNoVendor_PlansManufacture()
    {
        using var db = TestDbContextFactory.Create();
        var part = await SeedPartAsync(db, "MB-BUY-ROUTED", ProcurementSource.Buy);
        await SeedNineStepRoutingAsync(db, part.Id);
        await SeedSoLineAsync(db, part.Id, 500, daysOut: 60);

        var run = await Mrp(db).ExecuteRunAsync(new MrpRunOptions(PartIds: [part.Id]));

        var order = await PlannedOrderAsync(db, run, part.Id);
        order.OrderType.Should().Be(MrpOrderType.Manufacture);
        order.StartDate.Should().Be(order.DueDate.AddDays(-NineStepMakeDaysAt500));
    }

    [Fact]
    public async Task SubcontractPart_WithBom_PlansPurchase()
    {
        using var db = TestDbContextFactory.Create();
        var part = await SeedPartAsync(db, "MB-SUB", ProcurementSource.Subcontract);
        var component = await SeedPartAsync(db, "MB-SUB-COMP", ProcurementSource.Buy);
        db.BOMLines.Add(new BOMLine { ParentPartId = part.Id, ChildPartId = component.Id, Quantity = 1, SortOrder = 1 });
        await db.SaveChangesAsync();
        await SeedSoLineAsync(db, part.Id, 20, daysOut: 60);

        var run = await Mrp(db).ExecuteRunAsync(new MrpRunOptions(PartIds: [part.Id, component.Id]));

        (await PlannedOrderAsync(db, run, part.Id)).OrderType.Should().Be(MrpOrderType.Purchase);
    }

    [Fact]
    public async Task MakePart_StartDate_ComesFromTheRoutingAtTheOrderQuantity()
    {
        using var db = TestDbContextFactory.Create();
        var part = await SeedPartAsync(db, "MB-MAKE-ROUTED", ProcurementSource.Make);
        await SeedNineStepRoutingAsync(db, part.Id);
        await SeedPreferredVendorAsync(db, part.Id, leadTimeDays: 30);
        await SeedSoLineAsync(db, part.Id, 500, daysOut: 60);

        var run = await Mrp(db).ExecuteRunAsync(new MrpRunOptions(PartIds: [part.Id]));

        var order = await PlannedOrderAsync(db, run, part.Id);
        order.OrderType.Should().Be(MrpOrderType.Manufacture);
        order.Quantity.Should().Be(500);
        order.DueDate.Should().Be(Now.AddDays(60));
        order.StartDate.Should().Be(Now.AddDays(60 - NineStepMakeDaysAt500));
    }

    [Fact]
    public async Task BackwardSchedule_MakeChild_UsesTheRoutingMakeLeadTime()
    {
        using var db = TestDbContextFactory.Create();
        var parent = await SeedPartAsync(db, "MB-BS-PARENT", ProcurementSource.Make);
        var madeChild = await SeedPartAsync(db, "MB-BS-MADE", ProcurementSource.Make);
        var boughtChild = await SeedPartAsync(db, "MB-BS-BOUGHT", ProcurementSource.Buy);
        await SeedNineStepRoutingAsync(db, madeChild.Id);
        db.BOMLines.AddRange(
            new BOMLine
            {
                ParentPartId = parent.Id,
                ChildPartId = madeChild.Id,
                Quantity = 2,
                SourceType = BOMSourceType.Make,
                SortOrder = 1,
            },
            new BOMLine
            {
                ParentPartId = parent.Id,
                ChildPartId = boughtChild.Id,
                Quantity = 1,
                SourceType = BOMSourceType.Buy,
                LeadTimeDays = 3,
                SortOrder = 2,
            });
        await db.SaveChangesAsync();
        var line = await SeedSoLineAsync(db, parent.Id, 250, daysOut: 90);

        var service = new BackwardSchedulingService(db, Clock(), new PartSourcingResolver(db));
        var schedule = await service.CalculateSchedule(line.Id, CancellationToken.None);

        (schedule.MaterialsNeededBy - schedule.PoOrderBy).TotalDays.Should().Be(NineStepMakeDaysAt500);
    }
}
