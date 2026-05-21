using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Forge.Api.Features.Shipments;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Shipments;

/// <summary>
/// INV-SH2 regression: shipping a shipment must relieve inventory exactly once per line.
///
/// ShipShipmentHandler currently only flips status → ShippedDate. It never touches
/// bin_contents or bin_movements. These tests prove the −Σshipments term is structurally
/// absent, so any future implementation can use them as its acceptance criteria.
///
/// Expected state: RED until inventory relief is implemented in ShipShipmentHandler.
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
    /// Prove the relief-absent bug: after ShipShipment, bin_contents.quantity is unchanged.
    /// This test documents current (broken) behavior so the fix has a clear before/after.
    /// GREEN before fix (asserts quantity unchanged = confirms bug exists).
    /// Must be updated to assert quantity DECREASED when the fix lands.
    /// </summary>
    [Fact]
    public async Task ShipShipment_DoesNotRelieveInventory_CharacterizesAbsentReliefBug()
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
            EntityType = "Part",
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

        // Assert — characterize the bug: inventory is NOT relieved
        _db.ChangeTracker.Clear();
        var binAfter = await _db.BinContents.FindAsync(bin.Id);

        // This assertion DOCUMENTS THE BUG:
        // bin quantity is still 10 because ShipShipment never decrements it.
        // When the fix lands, change this to: binAfter!.Quantity.Should().Be(initialStock - shipQty)
        binAfter!.Quantity.Should().Be(initialStock,
            "INV-SH2 characterization: ShipShipmentHandler does not relieve inventory. " +
            "Quantity remains at {0} instead of being reduced by {1}. " +
            "This test must be updated to assert (initialStock - shipQty) once the fix lands.",
            initialStock, shipQty);
    }

    /// <summary>
    /// Prove the relief-absent bug via bin_movements: no Ship movement is recorded.
    /// This is the SQL-probe INV-SH2 expressed as a unit test.
    /// GREEN before fix (asserts 0 movements = confirms bug exists).
    /// Must be updated to assert count=1 when the fix lands.
    /// </summary>
    [Fact]
    public async Task ShipShipment_CreatesNoBinMovements_CharacterizesAbsentAuditTrail()
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
            EntityType = "Part",
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

        // Assert — no bin_movement with reason=Ship was created
        _db.ChangeTracker.Clear();
        var shipMovements = _db.BinMovements
            .Where(bm => bm.Reason == BinMovementReason.Ship)
            .ToList();

        // Documents the bug: 0 Ship movements recorded.
        // When fix lands, change to: shipMovements.Should().HaveCount(1)
        shipMovements.Should().BeEmpty(
            "INV-SH2 characterization: ShipShipmentHandler writes no BinMovement with reason=Ship. " +
            "This test must be updated to assert exactly 1 movement per shipment line once the fix lands.");
    }

    public void Dispose() => _db.Dispose();
}
