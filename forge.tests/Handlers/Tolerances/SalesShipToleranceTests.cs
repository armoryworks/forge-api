using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Features.Invoices;
using Forge.Api.Features.Shipments;
using Forge.Api.Features.Tolerances;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Core.Settings;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Data.Services;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Tolerances;

public class SalesShipToleranceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly IClock _clock = TestCaller.ClockAt(Now);

    private async Task<(SalesOrder Order, SalesOrderLine Line, Part Part)> SeedAsync(
        decimal quantity = 100m, decimal? customerOver = null, decimal? customerUnder = null,
        decimal? lineOver = null, decimal? lineUnder = null)
    {
        var customer = new Customer { Name = "Powersports", OverShipTolerancePct = customerOver, UnderShipTolerancePct = customerUnder };
        var part = new Part { PartNumber = "BRK-1", Description = "Bracket", Status = PartStatus.Active, ProcurementSource = ProcurementSource.Make, IsMrpPlanned = true, LotSizingRule = LotSizingRule.LotForLot };
        _db.Customers.Add(customer);
        _db.Parts.Add(part);
        await _db.SaveChangesAsync();
        var line = new SalesOrderLine
        {
            PartId = part.Id, Description = "Bracket", Quantity = quantity, UnitPrice = 5m, LineNumber = 1,
            OverShipTolerancePct = lineOver, UnderShipTolerancePct = lineUnder,
        };
        var order = new SalesOrder
        {
            OrderNumber = "SO-TOL-1", CustomerId = customer.Id, Status = SalesOrderStatus.Confirmed,
            RequestedDeliveryDate = Now.AddDays(20), Lines = [line],
        };
        _db.SalesOrders.Add(order);
        await _db.SaveChangesAsync();
        return (order, line, part);
    }

    private CreateShipmentHandler CreateHandler(params string[] roles)
        => new(new ShipmentRepository(_db), new SalesOrderRepository(_db), new Mock<IMediator>().Object,
            TestCaller.As(1, roles), db: _db);

    private async Task<int> ShipAsync(SalesOrder order, SalesOrderLine line, decimal quantity,
        bool confirm = false, string? reason = null, string[]? roles = null)
    {
        var created = await CreateHandler(roles ?? []).Handle(new CreateShipmentCommand(order.Id, null, null, null, null, null, null,
            [new CreateShipmentLineModel(line.Id, quantity, null, ConfirmOverTolerance: confirm, OverToleranceReason: reason)]),
            CancellationToken.None);
        await new ShipShipmentHandler(new ShipmentRepository(_db), httpContext: TestCaller.As(1), db: _db)
            .Handle(new ShipShipmentCommand(created.Id), CancellationToken.None);
        return created.Id;
    }

    private Task<SalesOrderLine> LineAsync(int id) => _db.SalesOrderLines.AsNoTracking().SingleAsync(l => l.Id == id);
    private Task<SalesOrder> OrderAsync(int id) => _db.SalesOrders.AsNoTracking().SingleAsync(o => o.Id == id);

    [Fact]
    public async Task NoTolerance_ShortShipment_LeavesTheLineOnBackOrder()
    {
        var (order, line, _) = await SeedAsync();

        await ShipAsync(order, line, 95m);

        var after = await LineAsync(line.Id);
        after.ShortClosedQuantity.Should().Be(0m);
        after.IsFullyShipped.Should().BeFalse();
        (await OrderAsync(order.Id)).Status.Should().Be(SalesOrderStatus.PartiallyShipped);
    }

    [Fact]
    public async Task NoTolerance_OverShipment_IsRefused()
    {
        var (order, line, _) = await SeedAsync();

        var act = () => CreateHandler().Handle(new CreateShipmentCommand(order.Id, null, null, null, null, null, null,
            [new CreateShipmentLineModel(line.Id, 101m, null)]), CancellationToken.None);

        (await act.Should().ThrowAsync<QuantityToleranceException>()).WithMessage("*only 100 remaining*");
    }

    [Fact]
    public async Task UnderInsideCustomerTolerance_ClosesTheLineAndShipsTheOrder()
    {
        var (order, line, _) = await SeedAsync(customerOver: 10m, customerUnder: 10m);

        await ShipAsync(order, line, 92m);

        var after = await LineAsync(line.Id);
        after.ShortClosedQuantity.Should().Be(8m);
        after.ShortCloseReason.Should().Be(SalesOrderLine.WithinToleranceReason);
        after.IsFullyShipped.Should().BeTrue();
        after.RemainingQuantity.Should().Be(0m);
        (await OrderAsync(order.Id)).Status.Should().Be(SalesOrderStatus.Shipped);
        (await _db.ActivityLogs.AsNoTracking().AnyAsync(a => a.Action == "line-closed-within-tolerance")).Should().BeTrue();
    }

    [Fact]
    public async Task UnderBelowCustomerTolerance_StaysOpen()
    {
        var (order, line, _) = await SeedAsync(customerOver: 10m, customerUnder: 10m);

        await ShipAsync(order, line, 89m);

        (await LineAsync(line.Id)).IsFullyShipped.Should().BeFalse();
    }

    [Fact]
    public async Task LineOverride_WinsOverTheCustomer()
    {
        var (order, line, _) = await SeedAsync(customerOver: 10m, customerUnder: 10m, lineOver: 0m, lineUnder: 0m);

        await ShipAsync(order, line, 95m);

        (await LineAsync(line.Id)).IsFullyShipped.Should().BeFalse();
    }

    [Fact]
    public async Task CompanyDefault_AppliesWhenNeitherLineNorCustomerSetsOne()
    {
        _db.SystemSettings.AddRange(
            new SystemSetting { Key = ToleranceSettings.OverShipPercentKey, Value = "5" },
            new SystemSetting { Key = ToleranceSettings.UnderShipPercentKey, Value = "5" });
        await _db.SaveChangesAsync();
        var (order, line, _) = await SeedAsync();

        await ShipAsync(order, line, 104m);

        (await LineAsync(line.Id)).ShippedQuantity.Should().Be(104m);
    }

    [Fact]
    public async Task OverInsideTolerance_ShipsWithoutAnOverride_AndCanBeInvoiced()
    {
        var (order, line, _) = await SeedAsync(customerOver: 10m, customerUnder: 10m);
        await ShipAsync(order, line, 108m);
        var job = new Job
        {
            JobNumber = "J-TOL", Title = "Brackets", TrackTypeId = 1, CurrentStageId = 1,
            CustomerId = order.CustomerId, SalesOrderLineId = line.Id, CompletedDate = Now.AddDays(-1),
        };
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();
        CreateInvoiceCommand? sent = null;
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<CreateInvoiceCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<InvoiceListItemModel>, CancellationToken>((cmd, _) => sent = (CreateInvoiceCommand)cmd)
            .ReturnsAsync(new InvoiceListItemModel(1, "INV-1", 1, "Powersports", "Draft", Now, Now, 0m, 0m, 0m, Now));

        await new CreateInvoiceFromJobHandler(_db, mediator.Object, _clock).Handle(new CreateInvoiceFromJobCommand(job.Id), CancellationToken.None);

        sent!.Lines.Single().Quantity.Should().Be(108m);
        (await _db.ShipmentLines.AsNoTracking().SingleAsync()).OverToleranceReason.Should().BeNull();
    }

    [Fact]
    public async Task UnderShipInsideTolerance_InvoicesWhatShipped()
    {
        var (order, line, _) = await SeedAsync(customerOver: 10m, customerUnder: 10m);
        await ShipAsync(order, line, 93m);
        var job = new Job
        {
            JobNumber = "J-TOL2", Title = "Brackets", TrackTypeId = 1, CurrentStageId = 1,
            CustomerId = order.CustomerId, SalesOrderLineId = line.Id, CompletedDate = Now.AddDays(-1),
        };
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();
        CreateInvoiceCommand? sent = null;
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<CreateInvoiceCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<InvoiceListItemModel>, CancellationToken>((cmd, _) => sent = (CreateInvoiceCommand)cmd)
            .ReturnsAsync(new InvoiceListItemModel(1, "INV-1", 1, "Powersports", "Draft", Now, Now, 0m, 0m, 0m, Now));

        await new CreateInvoiceFromJobHandler(_db, mediator.Object, _clock).Handle(new CreateInvoiceFromJobCommand(job.Id), CancellationToken.None);

        sent!.Lines.Single().Quantity.Should().Be(93m);
    }

    [Fact]
    public async Task OverAboveTolerance_NeedsAManagerToConfirmWithAReason()
    {
        var (order, line, _) = await SeedAsync(customerOver: 10m, customerUnder: 10m);

        var clerk = () => ShipAsync(order, line, 120m, confirm: true, reason: "Customer asked", roles: ["OfficeManager"]);
        (await clerk.Should().ThrowAsync<QuantityToleranceException>()).Which.Violation.CanOverride.Should().BeFalse();

        await ShipAsync(order, line, 120m, confirm: true, reason: "Customer asked for the overrun", roles: ["Manager"]);

        (await _db.ShipmentLines.AsNoTracking().SingleAsync()).OverToleranceReason.Should().Be("Customer asked for the overrun");
        (await LineAsync(line.Id)).ShippedQuantity.Should().Be(120m);
    }

    [Fact]
    public void RecordShipped_DoesNotCloseALineWhileMoreIsOnAPendingShipment()
    {
        var order = new SalesOrder { Id = 1, Status = SalesOrderStatus.Confirmed };
        var line = new SalesOrderLine { Id = 10, SalesOrderId = 1, Quantity = 100m, LineNumber = 1 };
        order.Lines.Add(line);
        AddShipment(order, ShipmentStatus.Shipped, 92m);
        AddShipment(order, ShipmentStatus.Pending, 8m);

        SalesOrderShipping.RecordShipped(order, _ => new QuantityTolerance(10m, 10m));

        line.ShortClosedQuantity.Should().Be(0m);
        order.Status.Should().Be(SalesOrderStatus.PartiallyShipped);
    }

    [Fact]
    public void RecordShipped_ReopensAToleranceCloseWhenAShipmentIsCancelled()
    {
        var order = new SalesOrder { Id = 1, Status = SalesOrderStatus.Confirmed };
        var line = new SalesOrderLine { Id = 10, SalesOrderId = 1, Quantity = 100m, LineNumber = 1 };
        order.Lines.Add(line);
        var first = AddShipment(order, ShipmentStatus.Shipped, 50m);
        AddShipment(order, ShipmentStatus.Shipped, 42m);
        SalesOrderShipping.RecordShipped(order, _ => new QuantityTolerance(10m, 10m));
        line.ShortClosedQuantity.Should().Be(8m);

        first.Status = ShipmentStatus.Cancelled;
        SalesOrderShipping.RecordShipped(order, _ => new QuantityTolerance(10m, 10m));

        line.ShortClosedQuantity.Should().Be(0m);
        line.ShortCloseReason.Should().BeNull();
        SalesOrderShipping.OpenToShip(order, line).Should().Be(58m);
    }

    [Fact]
    public async Task ClosedWithinTolerance_DropsOutOfMrpDemandAndAtpAllocation()
    {
        var (order, line, part) = await SeedAsync(customerOver: 10m, customerUnder: 10m);
        var second = new SalesOrderLine { SalesOrderId = order.Id, PartId = part.Id, Description = "Bracket", Quantity = 30m, UnitPrice = 5m, LineNumber = 2 };
        _db.SalesOrderLines.Add(second);
        await _db.SaveChangesAsync();
        await ShipAsync(order, line, 95m);

        var atp = await new AtpService(_db, _clock).CalculateAtpAsync(part.Id, 1m);
        atp.AllocatedToOrders.Should().Be(30m);

        var run = await new MrpService(_db, _clock, new PartSourcingResolver(_db), NullLogger<MrpService>.Instance)
            .ExecuteRunAsync(new MrpRunOptions());
        var demand = await _db.MrpDemands
            .Where(d => d.MrpRunId == run.Id && d.PartId == part.Id && d.Source == MrpDemandSource.SalesOrder)
            .SumAsync(d => d.Quantity);
        demand.Should().Be(30m);
    }

    private static Shipment AddShipment(SalesOrder order, ShipmentStatus status, decimal quantity)
    {
        var shipment = new Shipment { SalesOrderId = order.Id, ShipmentNumber = $"SH-{order.Shipments.Count + 1}", Status = status };
        shipment.Lines.Add(new ShipmentLine { SalesOrderLineId = 10, Quantity = quantity });
        order.Shipments.Add(shipment);
        return shipment;
    }
}
