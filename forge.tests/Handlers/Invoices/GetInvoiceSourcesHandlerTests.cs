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

    private async Task<SalesOrder> SeedOrderAsync(Customer customer, string orderNumber)
    {
        var order = new SalesOrder { OrderNumber = orderNumber, CustomerId = customer.Id, Status = SalesOrderStatus.Confirmed };
        _db.SalesOrders.Add(order);
        await _db.SaveChangesAsync();
        return order;
    }

    private async Task<Shipment> SeedShipmentAsync(SalesOrder order, string number, ShipmentStatus status)
    {
        var shipment = new Shipment { ShipmentNumber = number, SalesOrderId = order.Id, Status = status };
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
}
