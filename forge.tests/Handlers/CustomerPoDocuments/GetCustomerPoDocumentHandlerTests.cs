using FluentAssertions;

using Forge.Api.Features.CustomerPoDocuments;
using Forge.Core.Entities;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.CustomerPoDocuments;

/// <summary>
/// S4a — the customer-PO document is a LIVE view of the sales order, not a
/// snapshot: edits to the SO after generation must be reflected on re-read.
/// </summary>
public class GetCustomerPoDocumentHandlerTests
{
    [Fact]
    public async Task Get_ReturnsDocumentIdentity_AndLiveOrderData()
    {
        using var db = TestDbContextFactory.Create();
        var order = await SeedOrderWithDocumentAsync(db);
        var handler = new GetCustomerPoDocumentHandler(db);

        var result = await handler.Handle(
            new GetCustomerPoDocumentQuery(order.Id), CancellationToken.None);

        result.DocumentNumber.Should().Be("CPO-00001");
        result.OrderNumber.Should().Be("SO-77001");
        result.Status.Should().Be("Draft");
        result.CustomerName.Should().Be("Live View Co");
        result.CustomerPO.Should().Be("PO-CUST-9");
        result.Lines.Should().HaveCount(2);
        result.Subtotal.Should().Be(10 * 25m + 5 * 50m);        // 500
        result.TaxAmount.Should().Be(500m * 0.10m);             // 50
        result.Total.Should().Be(550m);
        result.ShippingAddress.Should().Contain("100 Forge Way").And.Contain("Springfield");
    }

    [Fact]
    public async Task Get_ReflectsSalesOrderEdits_OnReRead()
    {
        using var db = TestDbContextFactory.Create();
        var order = await SeedOrderWithDocumentAsync(db);
        var handler = new GetCustomerPoDocumentHandler(db);

        var before = await handler.Handle(
            new GetCustomerPoDocumentQuery(order.Id), CancellationToken.None);

        // Edit the SO after the document was generated: bump a line quantity.
        var line = db.SalesOrderLines.First(l => l.SalesOrderId == order.Id && l.LineNumber == 1);
        line.Quantity = 40; // was 10 @ 25 → +750 on the subtotal
        await db.SaveChangesAsync();

        var after = await handler.Handle(
            new GetCustomerPoDocumentQuery(order.Id), CancellationToken.None);

        after.DocumentNumber.Should().Be(before.DocumentNumber, "the identity is fixed");
        after.Lines.Single(l => l.LineNumber == 1).Quantity.Should().Be(40);
        after.Subtotal.Should().Be(40 * 25m + 5 * 50m);   // 1250
        after.Total.Should().Be(1250m * 1.10m);           // live totals follow the SO
        after.Subtotal.Should().NotBe(before.Subtotal);
    }

    [Fact]
    public async Task Get_NoDocumentForOrder_ThrowsKeyNotFound()
    {
        using var db = TestDbContextFactory.Create();
        var handler = new GetCustomerPoDocumentHandler(db);

        var act = () => handler.Handle(
            new GetCustomerPoDocumentQuery(4242), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*4242*");
    }

    private static async Task<SalesOrder> SeedOrderWithDocumentAsync(AppDbContext db)
    {
        var customer = new Customer { Name = "Live View Co", Email = "orders@liveview.example" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var address = new CustomerAddress
        {
            CustomerId = customer.Id,
            Label = "Main",
            Line1 = "100 Forge Way",
            City = "Springfield",
            State = "IL",
            PostalCode = "62701",
        };
        db.CustomerAddresses.Add(address);
        await db.SaveChangesAsync();

        var order = new SalesOrder
        {
            OrderNumber = "SO-77001",
            CustomerId = customer.Id,
            ShippingAddressId = address.Id,
            CustomerPO = "PO-CUST-9",
            TaxRate = 0.10m,
        };
        order.Lines.Add(new SalesOrderLine { Description = "Widget A", Quantity = 10, UnitPrice = 25m, LineNumber = 1 });
        order.Lines.Add(new SalesOrderLine { Description = "Widget B", Quantity = 5, UnitPrice = 50m, LineNumber = 2 });
        db.SalesOrders.Add(order);
        await db.SaveChangesAsync();

        db.CustomerPoDocuments.Add(new CustomerPoDocument
        {
            SalesOrderId = order.Id,
            DocumentNumber = "CPO-00001",
            GeneratedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        return order;
    }
}
