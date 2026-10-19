using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Forge.Api.Features.Shipments;
using Forge.Api.Features.Webhooks;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Shipments;

/// <summary>
/// Shipped quantity moves when a shipment ships, never when it is created, and exactly once (INV-SH1).
///
/// The CreateShipmentHandlerTests mock IMediator, so OnShipmentCreated_UpdateSalesOrder never runs there.
/// This test exercises the real notification path by wiring a real IMediator with the handlers sharing one
/// AppDbContext — the same isolation boundary as a live HTTP request.
/// </summary>
public class CreateShipmentMediatorIntegrationTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IServiceProvider _sp;

    public CreateShipmentMediatorIntegrationTests()
    {
        _db = TestDbContextFactory.Create();

        var services = new ServiceCollection();

        // Single DbContext — replicates request-scope sharing between CreateShipmentHandler
        // and OnShipmentCreated_UpdateSalesOrder (both get the same tracked instance).
        services.AddSingleton(_db);
        services.AddScoped<ISalesOrderRepository, SalesOrderRepository>();
        services.AddScoped<IShipmentRepository, ShipmentRepository>();

        // Real MediatR — registers CreateShipmentHandler + OnShipmentCreated_UpdateSalesOrder.
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssemblies(typeof(CreateShipmentHandler).Assembly));

        // IHttpContextAccessor — needed by CreateShipmentHandler to extract userId.
        var claims = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
                [new System.Security.Claims.Claim(
                    System.Security.Claims.ClaimTypes.NameIdentifier, "1")]));
        var httpCtx = new Mock<IHttpContextAccessor>();
        httpCtx.Setup(x => x.HttpContext).Returns(new DefaultHttpContext { User = claims });
        services.AddSingleton(httpCtx.Object);

        services.AddSingleton(Mock.Of<IWebhookEventPublisher>());
        services.AddSingleton(Mock.Of<IClock>());

        services.AddLogging();

        _sp = services.BuildServiceProvider();
    }

    private async Task<(SalesOrder Order, int LineId)> SeedOrderAsync(decimal quantity)
    {
        var customer = new Customer { Name = "INV-SH1 Test Customer" };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();

        var so = new SalesOrder
        {
            OrderNumber = "SO-SH1-001",
            CustomerId = customer.Id,
            Status = SalesOrderStatus.Confirmed,
            Lines =
            [
                new SalesOrderLine
                {
                    Description = "Test Part",
                    Quantity = quantity,
                    UnitPrice = 10m,
                    LineNumber = 1,
                    ShippedQuantity = 0m,
                }
            ]
        };
        _db.SalesOrders.Add(so);
        await _db.SaveChangesAsync();
        return (so, so.Lines.First().Id);
    }

    [Fact]
    public async Task CreateShipment_LeavesShippedQuantityAndOrderStatus_UntilTheShipmentShips()
    {
        var (so, soLineId) = await SeedOrderAsync(20m);
        var mediator = _sp.GetRequiredService<IMediator>();

        var created = await mediator.Send(new CreateShipmentCommand(
            so.Id, null, null, null, null, null, null,
            [new CreateShipmentLineModel(soLineId, 5m, null)]));

        _db.ChangeTracker.Clear();
        (await _db.Set<SalesOrderLine>().FindAsync(soLineId))!.ShippedQuantity.Should().Be(0m);
        (await _db.SalesOrders.FindAsync(so.Id))!.Status.Should().Be(SalesOrderStatus.Confirmed);

        await mediator.Send(new ShipShipmentCommand(created.Id));

        _db.ChangeTracker.Clear();
        (await _db.Set<SalesOrderLine>().FindAsync(soLineId))!.ShippedQuantity.Should().Be(5m,
            "ShippedQuantity must count the shipped shipment exactly once (INV-SH1)");
        (await _db.SalesOrders.FindAsync(so.Id))!.Status.Should().Be(SalesOrderStatus.PartiallyShipped);
    }

    [Fact]
    public async Task CreateShipment_CountsQuantityOnAPendingShipment_AsTaken()
    {
        var (so, soLineId) = await SeedOrderAsync(20m);
        var mediator = _sp.GetRequiredService<IMediator>();
        await mediator.Send(new CreateShipmentCommand(
            so.Id, null, null, null, null, null, null,
            [new CreateShipmentLineModel(soLineId, 15m, null)]));
        _db.ChangeTracker.Clear();

        var act = () => mediator.Send(new CreateShipmentCommand(
            so.Id, null, null, null, null, null, null,
            [new CreateShipmentLineModel(soLineId, 10m, null)]));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*only 5 remaining*");
    }

    [Fact]
    public async Task ShipShipment_PendingShipmentInflatedAtCreate_CountsOnlyWhatShipped()
    {
        var (so, soLineId) = await SeedOrderAsync(20m);
        var line = so.Lines.First();
        var shipNow = new Shipment { ShipmentNumber = "SH-OLD-1", SalesOrderId = so.Id, Status = ShipmentStatus.Pending };
        shipNow.Lines.Add(new ShipmentLine { SalesOrderLineId = soLineId, Quantity = 12m });
        var shipLater = new Shipment { ShipmentNumber = "SH-OLD-2", SalesOrderId = so.Id, Status = ShipmentStatus.Pending };
        shipLater.Lines.Add(new ShipmentLine { SalesOrderLineId = soLineId, Quantity = 8m });
        _db.Shipments.AddRange(shipNow, shipLater);
        line.ShippedQuantity = 20m;
        so.Status = SalesOrderStatus.Shipped;
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        await _sp.GetRequiredService<IMediator>().Send(new ShipShipmentCommand(shipNow.Id));

        _db.ChangeTracker.Clear();
        (await _db.Set<SalesOrderLine>().FindAsync(soLineId))!.ShippedQuantity.Should().Be(12m);
        (await _db.SalesOrders.FindAsync(so.Id))!.Status.Should().Be(SalesOrderStatus.PartiallyShipped);
    }

    public void Dispose() => _db.Dispose();
}
