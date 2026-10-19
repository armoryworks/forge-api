using FluentAssertions;

using Forge.Api.Features.Payments;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Payments;

public class ServiceLineOrderCompletionTests
{
    private static readonly DateTimeOffset Today = new(2026, 10, 17, 0, 0, 0, TimeSpan.Zero);

    private static async Task<(SalesOrder order, Part service)> SeedServiceOrderAsync(
        AppDbContext db, SalesOrderStatus status, decimal orderedServiceQuantity = 1m)
    {
        var customer = new Customer { Name = "Acme" };
        var service = new Part { PartNumber = "NRE-T112", Name = "Tooling NRE", InventoryClass = InventoryClass.Service };
        db.Customers.Add(customer);
        db.Parts.Add(service);
        await db.SaveChangesAsync();

        var order = new SalesOrder { OrderNumber = "SO-0042", CustomerId = customer.Id, Status = status };
        order.Lines.Add(new SalesOrderLine
        {
            PartId = service.Id, Description = "Tooling NRE", Quantity = orderedServiceQuantity, UnitPrice = 6800m, LineNumber = 1,
        });
        db.SalesOrders.Add(order);
        await db.SaveChangesAsync();
        return (order, service);
    }

    private static async Task<Invoice> AddSentInvoiceAsync(AppDbContext db, SalesOrder order, int partId, decimal quantity)
    {
        var invoice = new Invoice
        {
            InvoiceNumber = $"INV-{db.Invoices.Count() + 1:00000}",
            CustomerId = order.CustomerId,
            SalesOrderId = order.Id,
            Status = InvoiceStatus.Sent,
            InvoiceDate = Today,
            DueDate = Today,
        };
        invoice.Lines.Add(new InvoiceLine { PartId = partId, Description = "Tooling NRE", Quantity = quantity, UnitPrice = 6800m, LineNumber = 1 });
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();
        return invoice;
    }

    private static Task PayInFullAsync(AppDbContext db, Invoice invoice) =>
        new CreatePaymentHandler(new PaymentRepository(db), new CustomerRepository(db), new InvoiceRepository(db), db).Handle(
            new CreatePaymentCommand(invoice.CustomerId, "Check", invoice.BalanceDue, Today, "CHK-1", null,
                [new CreatePaymentApplicationModel(invoice.Id, invoice.BalanceDue)]),
            CancellationToken.None);

    [Fact]
    public async Task Service_only_order_completes_once_its_service_line_is_invoiced_and_paid()
    {
        using var db = TestDbContextFactory.Create();
        var (order, service) = await SeedServiceOrderAsync(db, SalesOrderStatus.InProduction);
        var invoice = await AddSentInvoiceAsync(db, order, service.Id, 1m);

        await PayInFullAsync(db, invoice);

        order.Status.Should().Be(SalesOrderStatus.Completed);
    }

    [Fact]
    public async Task Order_stays_open_while_part_of_its_service_line_is_not_invoiced()
    {
        using var db = TestDbContextFactory.Create();
        var (order, service) = await SeedServiceOrderAsync(db, SalesOrderStatus.Confirmed, orderedServiceQuantity: 2m);
        var invoice = await AddSentInvoiceAsync(db, order, service.Id, 1m);

        await PayInFullAsync(db, invoice);

        order.Status.Should().Be(SalesOrderStatus.Confirmed);
    }

    [Fact]
    public async Task Order_with_an_unshipped_goods_line_does_not_complete()
    {
        using var db = TestDbContextFactory.Create();
        var (order, service) = await SeedServiceOrderAsync(db, SalesOrderStatus.InProduction);
        var goods = new Part { PartNumber = "FG-1", Name = "Valve body" };
        db.Parts.Add(goods);
        await db.SaveChangesAsync();
        order.Lines.Add(new SalesOrderLine { PartId = goods.Id, Description = "Valve body", Quantity = 5m, UnitPrice = 10m, LineNumber = 2 });
        await db.SaveChangesAsync();
        var invoice = await AddSentInvoiceAsync(db, order, service.Id, 1m);

        await PayInFullAsync(db, invoice);

        order.Status.Should().Be(SalesOrderStatus.InProduction);
    }
}
