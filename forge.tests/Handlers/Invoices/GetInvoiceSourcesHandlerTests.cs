using FluentAssertions;

using Forge.Api.Features.Invoices;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Invoices;

public class GetInvoiceSourcesHandlerTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();

    private async Task<SalesOrder> SeedOrderAsync(Customer customer, string orderNumber, SalesOrderStatus status = SalesOrderStatus.Confirmed)
    {
        var order = new SalesOrder { OrderNumber = orderNumber, CustomerId = customer.Id, Status = status };
        _db.SalesOrders.Add(order);
        await _db.SaveChangesAsync();
        return order;
    }

    private async Task<Shipment> SeedShipmentAsync(SalesOrder order, string number, ShipmentStatus status, Part? part = null, decimal quantity = 0m)
    {
        var shipment = new Shipment { ShipmentNumber = number, SalesOrderId = order.Id, Status = status };
        if (part is not null)
            shipment.Lines.Add(new ShipmentLine { PartId = part.Id, Quantity = quantity });
        _db.Shipments.Add(shipment);
        await _db.SaveChangesAsync();
        return shipment;
    }

    [Fact]
    public async Task Returns_the_customers_orders_and_shipped_uninvoiced_shipments()
    {
        var customer = new Customer { Name = "Acme" };
        var other = new Customer { Name = "Other" };
        _db.Customers.AddRange(customer, other);
        await _db.SaveChangesAsync();

        var order = await SeedOrderAsync(customer, "SO-00042");
        var otherOrder = await SeedOrderAsync(other, "SO-00099");
        await SeedShipmentAsync(order, "SHP-1", ShipmentStatus.Shipped);
        await SeedShipmentAsync(order, "SHP-2", ShipmentStatus.Delivered);
        await SeedShipmentAsync(order, "SHP-3", ShipmentStatus.Pending);
        var invoiced = await SeedShipmentAsync(order, "SHP-4", ShipmentStatus.Shipped);
        await SeedShipmentAsync(otherOrder, "SHP-5", ShipmentStatus.Shipped);
        _db.Invoices.Add(new Invoice { InvoiceNumber = "INV-1", CustomerId = customer.Id, SalesOrderId = order.Id, ShipmentId = invoiced.Id });
        await _db.SaveChangesAsync();

        var result = await new GetInvoiceSourcesHandler(_db).Handle(new GetInvoiceSourcesQuery(customer.Id), CancellationToken.None);

        result.SalesOrders.Should().ContainSingle()
            .Which.Should().Match<InvoiceSourceSalesOrderResponseModel>(o =>
                o.Id == order.Id && o.OrderNumber == "SO-00042" && o.CustomerName == "Acme");
        result.Shipments.Select(s => s.ShipmentNumber).Should().BeEquivalentTo(["SHP-1", "SHP-2"]);
        result.Shipments.Should().OnlyContain(s => s.SalesOrderId == order.Id && s.SalesOrderNumber == "SO-00042");
    }

    [Fact]
    public async Task Leaves_out_draft_cancelled_and_completed_orders_and_their_shipments()
    {
        var customer = new Customer { Name = "Acme" };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();

        var open = await SeedOrderAsync(customer, "SO-1", SalesOrderStatus.PartiallyShipped);
        await SeedOrderAsync(customer, "SO-2", SalesOrderStatus.Draft);
        var cancelled = await SeedOrderAsync(customer, "SO-3", SalesOrderStatus.Cancelled);
        var completed = await SeedOrderAsync(customer, "SO-4", SalesOrderStatus.Completed);
        await SeedShipmentAsync(open, "SHP-1", ShipmentStatus.Shipped);
        await SeedShipmentAsync(cancelled, "SHP-2", ShipmentStatus.Shipped);
        await SeedShipmentAsync(completed, "SHP-3", ShipmentStatus.Delivered);

        var result = await new GetInvoiceSourcesHandler(_db).Handle(new GetInvoiceSourcesQuery(customer.Id), CancellationToken.None);

        result.SalesOrders.Select(o => o.OrderNumber).Should().BeEquivalentTo(["SO-1"]);
        result.Shipments.Select(s => s.ShipmentNumber).Should().BeEquivalentTo(["SHP-1"]);
    }

    [Fact]
    public async Task Leaves_out_shipments_whose_order_is_fully_invoiced()
    {
        var customer = new Customer { Name = "Acme" };
        var part = new Part { PartNumber = "PN-1", Name = "Bracket" };
        _db.Customers.Add(customer);
        _db.Parts.Add(part);
        await _db.SaveChangesAsync();

        var billed = await SeedOrderAsync(customer, "SO-1", SalesOrderStatus.Shipped);
        var partlyBilled = await SeedOrderAsync(customer, "SO-2", SalesOrderStatus.Shipped);
        await SeedShipmentAsync(billed, "SHP-1", ShipmentStatus.Delivered, part, 5m);
        await SeedShipmentAsync(partlyBilled, "SHP-2", ShipmentStatus.Delivered, part, 5m);
        _db.Invoices.AddRange(
            new Invoice
            {
                InvoiceNumber = "INV-1", CustomerId = customer.Id, SalesOrderId = billed.Id,
                Lines = [new InvoiceLine { PartId = part.Id, Description = "Bracket", Quantity = 5m, LineNumber = 1 }],
            },
            new Invoice
            {
                InvoiceNumber = "INV-2", CustomerId = customer.Id, SalesOrderId = partlyBilled.Id,
                Lines = [new InvoiceLine { PartId = part.Id, Description = "Bracket", Quantity = 2m, LineNumber = 1 }],
            });
        await _db.SaveChangesAsync();

        var result = await new GetInvoiceSourcesHandler(_db).Handle(new GetInvoiceSourcesQuery(customer.Id), CancellationToken.None);

        result.Shipments.Select(s => s.ShipmentNumber).Should().BeEquivalentTo(["SHP-2"]);
        result.SalesOrders.Select(o => o.OrderNumber).Should().BeEquivalentTo(["SO-1", "SO-2"]);
    }

    [Fact]
    public async Task Caps_each_list()
    {
        var customer = new Customer { Name = "Acme" };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();
        for (var i = 0; i <= GetInvoiceSourcesHandler.MaxResults; i++)
        {
            var order = new SalesOrder { OrderNumber = $"SO-{i}", CustomerId = customer.Id, Status = SalesOrderStatus.Shipped };
            order.Shipments.Add(new Shipment { ShipmentNumber = $"SHP-{i}", Status = ShipmentStatus.Shipped });
            _db.SalesOrders.Add(order);
        }
        await _db.SaveChangesAsync();

        var result = await new GetInvoiceSourcesHandler(_db).Handle(new GetInvoiceSourcesQuery(customer.Id), CancellationToken.None);

        result.SalesOrders.Should().HaveCount(GetInvoiceSourcesHandler.MaxResults);
        result.Shipments.Should().HaveCount(GetInvoiceSourcesHandler.MaxResults);
    }
}
