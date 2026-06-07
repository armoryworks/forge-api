using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Forge.Api.Features.Shipments;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Shipments;

/// <summary>
/// INV-SH2 regression: shipping a shipment relieves inventory exactly once per line.
///
/// ShipShipmentHandler now relieves on-hand stock via <see cref="InventoryReliefService"/> before
/// flipping status → ShippedDate: a FIFO bin_contents decrement plus one bin_movements row
/// (reason = Ship) per line. These tests are the acceptance criteria for that −Σshipments term.
///
/// Expected state: GREEN now that inventory relief is wired into ShipShipmentHandler.
/// </summary>
public class ShipShipmentInventoryReliefTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IServiceProvider _sp;

    public ShipShipmentInventoryReliefTests()
    {
        _db = TestDbContextFactory.Create();

        var services = new ServiceCollection();

        services.AddSingleton(_db);
        services.AddScoped<IShipmentRepository, ShipmentRepository>();
        services.AddScoped<ISalesOrderRepository, SalesOrderRepository>();
        services.AddScoped<InventoryReliefService>();

        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssemblies(typeof(ShipShipmentHandler).Assembly));

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

    /// <summary>
    /// After ShipShipment, bin_contents.quantity is reduced by the shipped quantity (FIFO relief).
    /// </summary>
    [Fact]
    public async Task ShipShipment_RelievesInventory_DecrementsBinQuantity()
    {
        // Arrange — seed customer, SO, shipment, and bin stock for the part
        var customer = new Customer { Name = "INV-SH2 Test Customer" };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();

        var part = new Part { PartNumber = "P-SH2-001", Description = "Test Part" };
        _db.Parts.Add(part);
        await _db.SaveChangesAsync();

        var so = new SalesOrder
        {
            OrderNumber = "SO-SH2-001",
            CustomerId = customer.Id,
            Status = SalesOrderStatus.Confirmed,
            Lines =
            [
                new SalesOrderLine
                {
                    PartId = part.Id,
                    Description = "Test Part",
                    Quantity = 10m,
                    UnitPrice = 50m,
                    LineNumber = 1,
                    ShippedQuantity = 0m,
                }
            ]
        };
        _db.SalesOrders.Add(so);
        await _db.SaveChangesAsync();

        var soLine = so.Lines.First();

        // Bin stock: 10 units available
        const decimal initialStock = 10m;
        var bin = new BinContent
        {
            EntityType = "part",
            EntityId = part.Id,
            Quantity = initialStock,
            Status = BinContentStatus.Stored,
            PlacedAt = DateTimeOffset.UtcNow,
        };
        _db.BinContents.Add(bin);
        await _db.SaveChangesAsync();

        // Shipment in Pending state with one line shipping 5 units
        const decimal shipQty = 5m;
        var shipment = new Shipment
        {
            ShipmentNumber = "SH-SH2-001",
            SalesOrderId = so.Id,
            Status = ShipmentStatus.Pending,
            Lines =
            [
                new ShipmentLine
                {
                    SalesOrderLineId = soLine.Id,
                    Quantity = shipQty,
                }
            ]
        };
        _db.Shipments.Add(shipment);
        await _db.SaveChangesAsync();

        // Act
        var mediator = _sp.GetRequiredService<IMediator>();
        await mediator.Send(new ShipShipmentCommand(shipment.Id));

        // Assert — inventory was relieved: bin quantity dropped by the shipped quantity
        _db.ChangeTracker.Clear();
        var binAfter = await _db.BinContents.FindAsync(bin.Id);

        binAfter!.Quantity.Should().Be(initialStock - shipQty,
            "INV-SH2: ShipShipmentHandler relieves inventory — quantity drops from {0} by the shipped {1}.",
            initialStock, shipQty);
    }

    /// <summary>
    /// Relief leaves an audit trail: exactly one bin_movements row (reason = Ship) per shipped line.
    /// This is the SQL-probe INV-SH2 expressed as a unit test.
    /// </summary>
    [Fact]
    public async Task ShipShipment_CreatesBinMovement_PerShippedLine()
    {
        // Arrange
        var customer = new Customer { Name = "INV-SH2 Movement Test Customer" };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();

        var part = new Part { PartNumber = "P-SH2-002", Description = "Test Part 2" };
        _db.Parts.Add(part);
        await _db.SaveChangesAsync();

        var so = new SalesOrder
        {
            OrderNumber = "SO-SH2-002",
            CustomerId = customer.Id,
            Status = SalesOrderStatus.Confirmed,
            Lines =
            [
                new SalesOrderLine { PartId = part.Id, Description = "P2", Quantity = 20m, UnitPrice = 25m, LineNumber = 1 }
            ]
        };
        _db.SalesOrders.Add(so);
        await _db.SaveChangesAsync();

        var bin = new BinContent
        {
            EntityType = "part",
            EntityId = part.Id,
            Quantity = 20m,
            Status = BinContentStatus.Stored,
            PlacedAt = DateTimeOffset.UtcNow,
        };
        _db.BinContents.Add(bin);
        await _db.SaveChangesAsync();

        var shipment = new Shipment
        {
            ShipmentNumber = "SH-SH2-002",
            SalesOrderId = so.Id,
            Status = ShipmentStatus.Pending,
            Lines =
            [
                new ShipmentLine { SalesOrderLineId = so.Lines.First().Id, Quantity = 10m }
            ]
        };
        _db.Shipments.Add(shipment);
        await _db.SaveChangesAsync();

        // Act
        var mediator = _sp.GetRequiredService<IMediator>();
        await mediator.Send(new ShipShipmentCommand(shipment.Id));

        // Assert — exactly one bin_movement with reason=Ship for the single shipped line
        _db.ChangeTracker.Clear();
        var shipMovements = _db.BinMovements
            .Where(bm => bm.Reason == BinMovementReason.Ship)
            .ToList();

        shipMovements.Should().HaveCount(1,
            "INV-SH2: ShipShipmentHandler writes exactly one BinMovement (reason=Ship) per shipped line.");
        shipMovements[0].Quantity.Should().Be(-10m, "the movement records the relief as a negative delta");
    }

    public void Dispose() => _db.Dispose();
}
