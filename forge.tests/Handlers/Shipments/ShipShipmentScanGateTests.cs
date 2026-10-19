using FluentAssertions;
using Moq;

using Forge.Api.Features.Shipments;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Shipments;

public class ShipShipmentScanGateTests : IDisposable
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();

    public void Dispose() => _db.Dispose();

    private async Task<Shipment> SeedAsync(bool requiresScan, bool storeScanCode = true)
    {
        var customer = new Customer { Name = "Acme" };
        var part = new Part { PartNumber = "P-100", Description = "Bracket" };
        var carrier = new Carrier { Name = "Freight Co", RequiresScanToShip = requiresScan };
        _db.AddRange(customer, part, carrier);
        await _db.SaveChangesAsync();

        var order = new SalesOrder { OrderNumber = "SO-0001", CustomerId = customer.Id };
        _db.SalesOrders.Add(order);
        await _db.SaveChangesAsync();

        var shipment = new Shipment
        {
            ShipmentNumber = "SH-0001",
            SalesOrderId = order.Id,
            CarrierId = carrier.Id,
            Lines = [new ShipmentLine { PartId = part.Id, Quantity = 5 }],
        };
        _db.Shipments.Add(shipment);
        await _db.SaveChangesAsync();
        if (storeScanCode)
        {
            shipment.ScanCode = ShipmentScanCode.Compute(shipment.ShipmentNumber, shipment.Lines);
            await _db.SaveChangesAsync();
        }
        _db.ChangeTracker.Clear();
        return shipment;
    }

    private ShipShipmentHandler Handler() => new(new ShipmentRepository(_db), db: _db);

    private async Task<ShipmentStatus> StatusAsync(int id)
    {
        _db.ChangeTracker.Clear();
        return (await _db.Shipments.FindAsync(id))!.Status;
    }

    [Fact]
    public async Task Refuses_a_scan_carrier_shipment_without_a_scan()
    {
        var shipment = await SeedAsync(requiresScan: true);

        var act = () => Handler().Handle(new ShipShipmentCommand(shipment.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Scan the shipment's printed label*");
        (await StatusAsync(shipment.Id)).Should().Be(ShipmentStatus.Pending);
    }

    [Fact]
    public async Task Refuses_a_label_from_another_shipment()
    {
        var shipment = await SeedAsync(requiresScan: true);

        var act = () => Handler().Handle(new ShipShipmentCommand(shipment.Id, "v1.SH-0002.AAAAAAAAAAAA"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*doesn't match this shipment*");
        (await StatusAsync(shipment.Id)).Should().Be(ShipmentStatus.Pending);
    }

    [Fact]
    public async Task Ships_when_the_scanned_label_matches()
    {
        var shipment = await SeedAsync(requiresScan: true);
        var code = (await _db.Shipments.FindAsync(shipment.Id))!.ScanCode;

        await Handler().Handle(new ShipShipmentCommand(shipment.Id, code + "\r\n"), CancellationToken.None);

        (await StatusAsync(shipment.Id)).Should().Be(ShipmentStatus.Shipped);
    }

    [Fact]
    public async Task Ships_a_shipment_without_a_stored_code_against_the_code_printed_on_its_label()
    {
        var shipment = await SeedAsync(requiresScan: true, storeScanCode: false);
        var lines = _db.ShipmentLines.Where(l => l.ShipmentId == shipment.Id).ToList();
        var printed = ShipmentScanCode.Compute("SH-0001", lines);
        _db.ChangeTracker.Clear();

        await Handler().Handle(new ShipShipmentCommand(shipment.Id, printed), CancellationToken.None);

        (await StatusAsync(shipment.Id)).Should().Be(ShipmentStatus.Shipped);
    }

    [Fact]
    public async Task Ships_without_a_scan_when_the_carrier_does_not_require_one()
    {
        var shipment = await SeedAsync(requiresScan: false);

        await Handler().Handle(new ShipShipmentCommand(shipment.Id), CancellationToken.None);

        (await StatusAsync(shipment.Id)).Should().Be(ShipmentStatus.Shipped);
    }

    [Fact]
    public async Task Detail_tells_the_client_when_the_carrier_requires_the_label_scan()
    {
        var shipment = new Shipment
        {
            Id = 1,
            ShipmentNumber = "SH-0001",
            SalesOrderId = 1,
            SalesOrder = new SalesOrder { Id = 1, OrderNumber = "SO-0001", CustomerId = 1, Customer = new Customer { Id = 1, Name = "Acme" } },
            CarrierId = 5,
            AssignedCarrier = new Carrier { Id = 5, Name = "Freight Co", RequiresScanToShip = true },
        };
        var repo = new Mock<IShipmentRepository>();
        repo.Setup(r => r.FindWithDetailsAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(shipment);

        var result = await new GetShipmentByIdHandler(repo.Object)
            .Handle(new GetShipmentByIdQuery(1), CancellationToken.None);

        result.CarrierRequiresScanToShip.Should().BeTrue();
    }
}
