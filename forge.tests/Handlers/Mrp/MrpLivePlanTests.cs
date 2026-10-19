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
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Mrp;

public class MrpLivePlanTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 15, 0, 0, TimeSpan.Zero);

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly MrpService _mrp;

    public MrpLivePlanTests()
    {
        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(Now);
        _mrp = new MrpService(_db, clock.Object, new PartSourcingResolver(_db), NullLogger<MrpService>.Instance);
    }

    [Fact]
    public async Task Simulation_PlannedOrdersAndExceptions_StayOutOfTheLivePlan()
    {
        var part = await SeedPartAsync("SIM-1");
        await SeedSalesOrderAsync(part, 100m, Now.AddDays(-3));

        var real = await _mrp.ExecuteRunAsync(new MrpRunOptions());
        var simulation = await _mrp.ExecuteRunAsync(new MrpRunOptions(IsSimulation: true));

        var live = await new GetPlannedOrdersHandler(_db).Handle(new GetPlannedOrdersQuery(null, null), CancellationToken.None);
        live.Should().NotBeEmpty().And.OnlyContain(po => po.MrpRunId == real.Id);
        var preview = await new GetPlannedOrdersHandler(_db).Handle(new GetPlannedOrdersQuery(simulation.Id, null), CancellationToken.None);
        preview.Should().NotBeEmpty();

        var exceptions = await new GetMrpExceptionsHandler(_db).Handle(new GetMrpExceptionsQuery(null, null), CancellationToken.None);
        exceptions.Should().NotBeEmpty().And.OnlyContain(e => e.MrpRunId == real.Id);
    }

    [Fact]
    public async Task Simulation_PlannedOrder_CannotBeFirmedOrReleased()
    {
        var part = await SeedPartAsync("SIM-2");
        await SeedSalesOrderAsync(part, 100m, Now.AddDays(30));
        var simulation = await _mrp.ExecuteRunAsync(new MrpRunOptions(IsSimulation: true));
        var order = await _db.MrpPlannedOrders.AsNoTracking().FirstAsync(po => po.MrpRunId == simulation.Id);

        var firm = () => new UpdatePlannedOrderHandler(_db).Handle(new UpdatePlannedOrderCommand(order.Id, true, null), CancellationToken.None);
        await firm.Should().ThrowAsync<InvalidOperationException>().WithMessage("*simulation*");

        var release = () => new ReleasePlannedOrderHandler(
                _db, null!, null!, null!, null!, null!, null!, null!)
            .Handle(new ReleasePlannedOrderCommand(order.Id), CancellationToken.None);
        await release.Should().ThrowAsync<InvalidOperationException>().WithMessage("*simulation*");
    }

    [Fact]
    public async Task SecondRealRun_SupersedesTheFirstRunsUnfirmedOrders()
    {
        var part = await SeedPartAsync("SUP-1");
        await SeedSalesOrderAsync(part, 100m, Now.AddDays(30));
        await SeedSalesOrderAsync(part, 50m, Now.AddDays(60));

        var first = await _mrp.ExecuteRunAsync(new MrpRunOptions());
        var firstOrders = await _db.MrpPlannedOrders.AsNoTracking().Where(po => po.MrpRunId == first.Id).OrderBy(po => po.DueDate).ToListAsync();
        firstOrders.Should().HaveCount(2);
        await new UpdatePlannedOrderHandler(_db).Handle(new UpdatePlannedOrderCommand(firstOrders[0].Id, true, null), CancellationToken.None);
        await _mrp.ExecuteRunAsync(new MrpRunOptions(IsSimulation: true));
        var second = await _mrp.ExecuteRunAsync(new MrpRunOptions());

        var live = await new GetPlannedOrdersHandler(_db).Handle(new GetPlannedOrdersQuery(null, null), CancellationToken.None);
        var open = live.Where(po => po.Status != MrpPlannedOrderStatus.Cancelled).ToList();
        open.Should().Contain(po => po.Id == firstOrders[0].Id && po.Status == MrpPlannedOrderStatus.Firmed);
        open.Where(po => po.Id != firstOrders[0].Id).Should().OnlyContain(po => po.MrpRunId == second.Id);
        open.Sum(po => po.Quantity).Should().Be(150m);
        live.Single(po => po.Id == firstOrders[1].Id).Status.Should().Be(MrpPlannedOrderStatus.Cancelled);
    }

    [Fact]
    public async Task MpsDemand_IsConsumedBySalesOrdersInItsBucket()
    {
        var part = await SeedPartAsync("MPS-C1");
        await SeedSalesOrderAsync(part, 29000m, new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero));
        var weekEnd = new DateTimeOffset(2026, 10, 25, 0, 0, 0, TimeSpan.Zero);
        _db.MasterSchedules.Add(new MasterSchedule
        {
            Name = "Weekly",
            Status = MasterScheduleStatus.Active,
            PeriodStart = new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero),
            PeriodEnd = weekEnd.AddDays(28),
            Lines = Enumerable.Range(0, 4)
                .Select(w => new MasterScheduleLine { PartId = part.Id, Quantity = 2000m, DueDate = weekEnd.AddDays(7 * w) })
                .ToList(),
        });
        await _db.SaveChangesAsync();

        var run = await _mrp.ExecuteRunAsync(new MrpRunOptions());

        var demand = await _db.MrpDemands.AsNoTracking().Where(d => d.MrpRunId == run.Id && d.PartId == part.Id).ToListAsync();
        demand.Sum(d => d.Quantity).Should().Be(35000m);
        demand.Where(d => d.Source == MrpDemandSource.MasterSchedule).Should().HaveCount(3);
    }

    [Fact]
    public async Task CompletedOrPastSchedules_FeedNoDemand()
    {
        var part = await SeedPartAsync("MPS-C2");
        _db.MasterSchedules.AddRange(
            new MasterSchedule
            {
                Name = "Done",
                Status = MasterScheduleStatus.Completed,
                PeriodStart = Now,
                PeriodEnd = Now.AddDays(30),
                Lines = [new MasterScheduleLine { PartId = part.Id, Quantity = 500m, DueDate = Now.AddDays(10) }],
            },
            new MasterSchedule
            {
                Name = "Last quarter",
                Status = MasterScheduleStatus.Active,
                PeriodStart = Now.AddDays(-120),
                PeriodEnd = Now.AddDays(-30),
                Lines = [new MasterScheduleLine { PartId = part.Id, Quantity = 700m, DueDate = Now.AddDays(-40) }],
            });
        await _db.SaveChangesAsync();

        var run = await _mrp.ExecuteRunAsync(new MrpRunOptions());

        (await _db.MrpDemands.AnyAsync(d => d.MrpRunId == run.Id)).Should().BeFalse();
    }

    private async Task<Part> SeedPartAsync(string partNumber)
    {
        var part = new Part
        {
            PartNumber = partNumber,
            Description = partNumber,
            Status = PartStatus.Active,
            IsMrpPlanned = true,
            LotSizingRule = LotSizingRule.LotForLot,
        };
        _db.Parts.Add(part);
        await _db.SaveChangesAsync();
        return part;
    }

    private async Task SeedSalesOrderAsync(Part part, decimal quantity, DateTimeOffset requested)
    {
        var customer = new Customer { Name = $"Customer {Guid.NewGuid():N}" };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();
        var order = new SalesOrder
        {
            OrderNumber = $"SO-{Guid.NewGuid():N}"[..12],
            CustomerId = customer.Id,
            Status = SalesOrderStatus.Confirmed,
            RequestedDeliveryDate = requested,
        };
        _db.SalesOrders.Add(order);
        await _db.SaveChangesAsync();
        _db.SalesOrderLines.Add(new SalesOrderLine
        {
            SalesOrderId = order.Id,
            PartId = part.Id,
            Description = part.PartNumber,
            Quantity = quantity,
            UnitPrice = 1m,
        });
        await _db.SaveChangesAsync();
    }
}
