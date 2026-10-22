using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Shipments;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Shipments;

public class CancelShipmentHandlerTests : IDisposable
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();

    public void Dispose() => _db.Dispose();

    private async Task<(Shipment Shipment, SalesOrder Order)> SeedAsync(
        ShipmentStatus status = ShipmentStatus.Pending,
        SalesOrderStatus orderStatus = SalesOrderStatus.InProduction,
        decimal ordered = 10m,
        decimal shippedBefore = 0m,
        decimal shipping = 4m,
        CarrierIntegrationKind? carrierKind = null,
        string? trackingNumber = null,
        string? pickup = null)
    {
        var customer = new Customer { Name = "Acme" };
        var part = new Part { PartNumber = "P-100", Description = "Bracket" };
        _db.AddRange(customer, part);
        Carrier? carrier = null;
        if (carrierKind.HasValue)
        {
            carrier = new Carrier { Name = "Parcel Co", IntegrationKind = carrierKind.Value };
            _db.Add(carrier);
        }
        await _db.SaveChangesAsync();

        var order = new SalesOrder
        {
            OrderNumber = "SO-0001",
            CustomerId = customer.Id,
            Status = orderStatus,
            Lines = [new SalesOrderLine { PartId = part.Id, Description = "Bracket", Quantity = ordered, ShippedQuantity = shippedBefore }],
        };
        _db.SalesOrders.Add(order);
        await _db.SaveChangesAsync();

        if (shippedBefore > 0)
        {
            _db.Shipments.Add(new Shipment
            {
                ShipmentNumber = "SH-0000",
                SalesOrderId = order.Id,
                Status = ShipmentStatus.Shipped,
                Lines = [new ShipmentLine { SalesOrderLineId = order.Lines.First().Id, PartId = part.Id, Quantity = shippedBefore }],
            });
            await _db.SaveChangesAsync();
        }

        var shipment = new Shipment
        {
            ShipmentNumber = "SH-0001",
            SalesOrderId = order.Id,
            Status = status,
            CarrierId = carrier?.Id,
            TrackingNumber = trackingNumber,
            PickupConfirmationNumber = pickup,
            Lines = [new ShipmentLine { SalesOrderLineId = order.Lines.First().Id, PartId = part.Id, Quantity = shipping }],
        };
        _db.Shipments.Add(shipment);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return (shipment, order);
    }

    private Task Cancel(int id) => new CancelShipmentHandler(_db).Handle(new CancelShipmentCommand(id), CancellationToken.None);

    [Theory]
    [InlineData(ShipmentStatus.Pending)]
    [InlineData(ShipmentStatus.Packed)]
    public async Task Cancels_an_unshipped_shipment_and_gives_the_quantity_back_to_the_order(ShipmentStatus status)
    {
        var (shipment, order) = await SeedAsync(status);

        await Cancel(shipment.Id);

        _db.ChangeTracker.Clear();
        (await _db.Shipments.SingleAsync(s => s.Id == shipment.Id)).Status.Should().Be(ShipmentStatus.Cancelled);
        var reloaded = await _db.SalesOrders.Include(o => o.Lines).Include(o => o.Shipments).ThenInclude(s => s.Lines)
            .SingleAsync(o => o.Id == order.Id);
        reloaded.Lines.Single().ShippedQuantity.Should().Be(0m);
        SalesOrderShipping.OpenToShip(reloaded, reloaded.Lines.Single()).Should().Be(10m);
        reloaded.Status.Should().Be(SalesOrderStatus.InProduction);
        (await _db.ActivityLogs.AnyAsync(a => a.Action == "shipment-cancelled" && a.EntityType == "Shipment" && a.EntityId == shipment.Id))
            .Should().BeTrue();
    }

    [Fact]
    public async Task Order_with_an_earlier_shipment_stays_partially_shipped()
    {
        var (shipment, order) = await SeedAsync(orderStatus: SalesOrderStatus.PartiallyShipped, shippedBefore: 3m);

        await Cancel(shipment.Id);

        _db.ChangeTracker.Clear();
        var reloaded = await _db.SalesOrders.Include(o => o.Lines).SingleAsync(o => o.Id == order.Id);
        reloaded.Lines.Single().ShippedQuantity.Should().Be(3m);
        reloaded.Status.Should().Be(SalesOrderStatus.PartiallyShipped);
    }

    [Fact]
    public async Task Order_marked_shipped_with_nothing_shipped_returns_to_in_production_when_it_was_in_production()
    {
        var (shipment, order) = await SeedAsync(orderStatus: SalesOrderStatus.PartiallyShipped);
        _db.ActivityLogs.Add(new ActivityLog
        {
            EntityType = "SalesOrder",
            EntityId = order.Id,
            Action = "status_changed",
            Description = "Status changed to In Production (job JOB-0001 entered In Production).",
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        await Cancel(shipment.Id);

        _db.ChangeTracker.Clear();
        (await _db.SalesOrders.SingleAsync(o => o.Id == order.Id)).Status.Should().Be(SalesOrderStatus.InProduction);
    }

    [Fact]
    public async Task Order_marked_shipped_with_nothing_shipped_and_never_in_production_returns_to_confirmed()
    {
        var (shipment, order) = await SeedAsync(orderStatus: SalesOrderStatus.PartiallyShipped);

        await Cancel(shipment.Id);

        _db.ChangeTracker.Clear();
        (await _db.SalesOrders.SingleAsync(o => o.Id == order.Id)).Status.Should().Be(SalesOrderStatus.Confirmed);
    }

    [Theory]
    [InlineData(ShipmentStatus.Shipped)]
    [InlineData(ShipmentStatus.InTransit)]
    [InlineData(ShipmentStatus.Delivered)]
    [InlineData(ShipmentStatus.Cancelled)]
    public async Task Refuses_a_shipment_that_has_left_or_is_already_cancelled(ShipmentStatus status)
    {
        var (shipment, _) = await SeedAsync(status);

        var act = () => Cancel(shipment.Id);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Only Pending or Packed*");
    }

    [Fact]
    public async Task Refuses_when_a_carrier_label_was_bought()
    {
        var (shipment, _) = await SeedAsync(carrierKind: CarrierIntegrationKind.Api, trackingNumber: "1Z999");

        var act = () => Cancel(shipment.Id);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Void the label*");
    }

    [Fact]
    public async Task Allows_a_typed_tracking_number_on_a_manual_carrier()
    {
        var (shipment, _) = await SeedAsync(carrierKind: CarrierIntegrationKind.Manual, trackingNumber: "PRO-77");

        await Cancel(shipment.Id);

        _db.ChangeTracker.Clear();
        (await _db.Shipments.SingleAsync(s => s.Id == shipment.Id)).Status.Should().Be(ShipmentStatus.Cancelled);
    }

    [Fact]
    public async Task Refuses_when_a_pickup_is_scheduled()
    {
        var (shipment, _) = await SeedAsync(pickup: "PU-1");

        var act = () => Cancel(shipment.Id);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Cancel the pickup*");
    }

    [Fact]
    public async Task Refuses_when_an_open_invoice_bills_the_shipment_but_not_a_voided_one()
    {
        var (shipment, order) = await SeedAsync();
        _db.Invoices.Add(new Invoice { InvoiceNumber = "INV-1", ShipmentId = shipment.Id, SalesOrderId = order.Id, CustomerId = order.CustomerId, Status = InvoiceStatus.Voided });
        _db.Invoices.Add(new Invoice { InvoiceNumber = "INV-2", ShipmentId = shipment.Id, SalesOrderId = order.Id, CustomerId = order.CustomerId, Status = InvoiceStatus.Sent });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var act = () => Cancel(shipment.Id);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Invoice INV-2 bills this shipment*");
    }

    [Fact]
    public async Task Unknown_shipment_is_not_found()
    {
        var act = () => Cancel(404);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
