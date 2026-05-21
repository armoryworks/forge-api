using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Forge.Api.Features.Shipments;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Shipments;

/// <summary>
/// INV-SH1 regression: ShippedQuantity must increment exactly once per shipment.
///
/// The existing CreateShipmentHandlerTests mock IMediator, so OnShipmentCreated_UpdateSalesOrder
/// never runs and the double-count bug is invisible. This test exercises the real notification
/// path by wiring a real IMediator with both handlers sharing one AppDbContext — the same
/// isolation boundary as a live HTTP request.
///
/// Was red before the fix (double-count → ShippedQuantity == 2 × shipped); green after.
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

        services.AddLogging();

        _sp = services.BuildServiceProvider();
    }

    [Fact]
    public async Task CreateShipment_ShippedQuantityIncrementedExactlyOnce_NotDoubled()
    {
        // Arrange — seed minimal spine data (Customer → SalesOrder → Line)
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
                    Quantity = 20m,
                    UnitPrice = 10m,
                    LineNumber = 1,
                    ShippedQuantity = 0m,
                }
            ]
        };
        _db.SalesOrders.Add(so);
        await _db.SaveChangesAsync();

        var soLineId = so.Lines.First().Id;
        const decimal shipQty = 5m;

        // Act — dispatched through the real mediator; OnShipmentCreated_UpdateSalesOrder fires.
        var mediator = _sp.GetRequiredService<IMediator>();
        await mediator.Send(new CreateShipmentCommand(
            so.Id, null, null, null, null, null, null,
            [new CreateShipmentLineModel(soLineId, shipQty, null)]));

        // Assert — clear tracking so FindAsync hits the committed store, not the cache.
        _db.ChangeTracker.Clear();
        var refreshedLine = await _db.Set<SalesOrderLine>().FindAsync(soLineId);

        refreshedLine!.ShippedQuantity.Should().Be(shipQty,
            "ShippedQuantity must be incremented exactly once (INV-SH1). " +
            "A value of 2× indicates the handler double-counted.");
    }

    public void Dispose() => _db.Dispose();
}
