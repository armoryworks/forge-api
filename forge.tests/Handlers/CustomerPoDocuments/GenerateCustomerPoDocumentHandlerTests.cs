using FluentAssertions;

using Forge.Api.Features.CustomerPoDocuments;
using Forge.Core.Entities;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.CustomerPoDocuments;

/// <summary>
/// S4a — generation of the thin customer-PO identity record: CPO-{seq:D5}
/// numbering, idempotence per live sales order, and the mandatory activity
/// row on the SalesOrder.
/// </summary>
public class GenerateCustomerPoDocumentHandlerTests
{
    [Fact]
    public async Task Generate_MintsSequentialCpoNumbers_AcrossOrders()
    {
        using var db = TestDbContextFactory.Create();
        var handler = new GenerateCustomerPoDocumentHandler(db, new SystemClock());
        var (order1, _) = await SeedOrderAsync(db, "SO-00001");
        var (order2, _) = await SeedOrderAsync(db, "SO-00002");

        var first = await handler.Handle(
            new GenerateCustomerPoDocumentCommand(order1.Id), CancellationToken.None);
        var second = await handler.Handle(
            new GenerateCustomerPoDocumentCommand(order2.Id), CancellationToken.None);

        first.DocumentNumber.Should().Be("CPO-00001");
        second.DocumentNumber.Should().Be("CPO-00002");
        first.SalesOrderId.Should().Be(order1.Id);
        second.SalesOrderId.Should().Be(order2.Id);
    }

    [Fact]
    public async Task Generate_IsIdempotent_ReturnsExistingLiveRow()
    {
        using var db = TestDbContextFactory.Create();
        var handler = new GenerateCustomerPoDocumentHandler(db, new SystemClock());
        var (order, _) = await SeedOrderAsync(db, "SO-00010");

        var first = await handler.Handle(
            new GenerateCustomerPoDocumentCommand(order.Id, GeneratedFromQuoteId: null), CancellationToken.None);
        var again = await handler.Handle(
            new GenerateCustomerPoDocumentCommand(order.Id), CancellationToken.None);

        again.Id.Should().Be(first.Id);
        again.DocumentNumber.Should().Be(first.DocumentNumber);
        db.CustomerPoDocuments.Count(d => d.SalesOrderId == order.Id).Should().Be(1);
    }

    [Fact]
    public async Task Generate_WritesActivityLog_OnTheSalesOrder()
    {
        using var db = TestDbContextFactory.Create();
        var handler = new GenerateCustomerPoDocumentHandler(db, new SystemClock());
        var (order, _) = await SeedOrderAsync(db, "SO-00020");

        var result = await handler.Handle(
            new GenerateCustomerPoDocumentCommand(order.Id), CancellationToken.None);

        var log = db.ActivityLogs.Single(l => l.Action == "customer-po-generated");
        log.EntityType.Should().Be("SalesOrder");
        log.EntityId.Should().Be(order.Id);
        log.Description.Should().Contain(result.DocumentNumber);
        log.Description.Should().Contain(order.OrderNumber);
    }

    [Fact]
    public async Task Generate_CarriesGeneratedFromQuoteId()
    {
        using var db = TestDbContextFactory.Create();
        var handler = new GenerateCustomerPoDocumentHandler(db, new SystemClock());
        var (order, quote) = await SeedOrderAsync(db, "SO-00030", withQuote: true);

        var result = await handler.Handle(
            new GenerateCustomerPoDocumentCommand(order.Id, quote!.Id), CancellationToken.None);

        result.GeneratedFromQuoteId.Should().Be(quote.Id);
    }

    [Fact]
    public async Task Generate_UnknownSalesOrder_ThrowsKeyNotFound()
    {
        using var db = TestDbContextFactory.Create();
        var handler = new GenerateCustomerPoDocumentHandler(db, new SystemClock());

        var act = () => handler.Handle(
            new GenerateCustomerPoDocumentCommand(9999), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*9999*");
    }

    private static async Task<(SalesOrder Order, Quote? Quote)> SeedOrderAsync(
        Forge.Data.Context.AppDbContext db, string orderNumber, bool withQuote = false)
    {
        var customer = new Customer { Name = $"Customer for {orderNumber}" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        Quote? quote = null;
        if (withQuote)
        {
            quote = new Quote { QuoteNumber = $"QT-{orderNumber}", CustomerId = customer.Id };
            db.Quotes.Add(quote);
            await db.SaveChangesAsync();
        }

        var order = new SalesOrder
        {
            OrderNumber = orderNumber,
            CustomerId = customer.Id,
            QuoteId = quote?.Id,
            TaxRate = 0.07m,
        };
        order.Lines.Add(new SalesOrderLine
        {
            Description = "Widget",
            Quantity = 10,
            UnitPrice = 25m,
            LineNumber = 1,
        });
        db.SalesOrders.Add(order);
        await db.SaveChangesAsync();

        return (order, quote);
    }
}
