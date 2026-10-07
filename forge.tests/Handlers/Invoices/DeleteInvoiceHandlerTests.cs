using FluentAssertions;
using Moq;

using Forge.Api.Features.Invoices;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Invoices;

public class DeleteInvoiceHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly DeleteInvoiceHandler _handler;

    public DeleteInvoiceHandlerTests()
    {
        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(Now);
        _handler = new DeleteInvoiceHandler(new InvoiceRepository(_db), _db, clock.Object);
    }

    private async Task<(Customer Customer, Shipment Shipment)> SeedShipmentAsync()
    {
        var customer = new Customer { Name = "Acme" };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();
        var order = new SalesOrder { OrderNumber = "SO-00042", CustomerId = customer.Id, Status = SalesOrderStatus.Shipped };
        var shipment = new Shipment { ShipmentNumber = "SHP-00007", Status = ShipmentStatus.Delivered };
        order.Shipments.Add(shipment);
        _db.SalesOrders.Add(order);
        await _db.SaveChangesAsync();
        return (customer, shipment);
    }

    [Fact]
    public async Task Handle_DraftWithShipment_ReleasesTheShipmentAndLogsTheDelete()
    {
        var (customer, shipment) = await SeedShipmentAsync();
        var invoice = new Invoice
        {
            InvoiceNumber = "INV-0007", CustomerId = customer.Id, SalesOrderId = shipment.SalesOrderId,
            ShipmentId = shipment.Id, Status = InvoiceStatus.Draft,
        };
        _db.Invoices.Add(invoice);
        await _db.SaveChangesAsync();

        await _handler.Handle(new DeleteInvoiceCommand(invoice.Id), CancellationToken.None);

        invoice.DeletedAt.Should().Be(Now);
        invoice.ShipmentId.Should().BeNull();
        _db.ActivityLogs.Should().ContainSingle(a => a.EntityType == "Invoice" && a.EntityId == invoice.Id && a.Action == "deleted");

        var sources = await new GetInvoiceSourcesHandler(_db).Handle(new GetInvoiceSourcesQuery(customer.Id), CancellationToken.None);
        sources.Shipments.Should().ContainSingle(s => s.Id == shipment.Id);
    }

    [Fact]
    public async Task Handle_SentInvoice_IsRefused()
    {
        var (customer, shipment) = await SeedShipmentAsync();
        var invoice = new Invoice
        {
            InvoiceNumber = "INV-0008", CustomerId = customer.Id, ShipmentId = shipment.Id, Status = InvoiceStatus.Sent,
        };
        _db.Invoices.Add(invoice);
        await _db.SaveChangesAsync();

        var act = () => _handler.Handle(new DeleteInvoiceCommand(invoice.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Only Draft invoices can be deleted");
        invoice.ShipmentId.Should().Be(shipment.Id);
        invoice.DeletedAt.Should().BeNull();
    }
}
