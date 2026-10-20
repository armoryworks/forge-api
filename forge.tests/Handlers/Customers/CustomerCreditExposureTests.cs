using FluentAssertions;
using Moq;

using Forge.Api.Features.Customers;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Services;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Customers;

public class CustomerCreditExposureTests
{
    private static async Task<Customer> SeedCustomerAsync(AppDbContext db, decimal? creditLimit = 100_000m)
    {
        var customer = new Customer { Name = "Acme", CreditLimit = creditLimit };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        return customer;
    }

    private static SalesOrder Order(int customerId, decimal amount, SalesOrderStatus status = SalesOrderStatus.Confirmed)
    {
        var order = new SalesOrder { OrderNumber = $"SO-{Guid.NewGuid():N}", CustomerId = customerId, Status = status };
        order.Lines.Add(new SalesOrderLine { Description = "Widget", Quantity = 1, UnitPrice = amount, LineNumber = 1 });
        return order;
    }

    private static Invoice InvoiceFor(
        int customerId, decimal amount, InvoiceStatus status, int? salesOrderId = null, decimal paid = 0m,
        DateTimeOffset? invoiceDate = null)
    {
        var invoice = new Invoice
        {
            InvoiceNumber = $"INV-{Guid.NewGuid():N}",
            CustomerId = customerId,
            SalesOrderId = salesOrderId,
            Status = status,
            InvoiceDate = invoiceDate ?? new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero),
        };
        invoice.Lines.Add(new InvoiceLine { Description = "Widget", Quantity = 1, UnitPrice = amount, LineNumber = 1 });
        if (paid > 0)
            invoice.PaymentApplications.Add(new PaymentApplication
            {
                Amount = paid,
                Payment = new Payment { PaymentNumber = $"PAY-{Guid.NewGuid():N}", CustomerId = customerId, Amount = paid },
            });
        return invoice;
    }

    [Fact]
    public async Task Invoiced_part_of_an_order_is_not_counted_twice()
    {
        using var db = TestDbContextFactory.Create();
        var customer = await SeedCustomerAsync(db);
        var order = Order(customer.Id, 1_000m);
        db.SalesOrders.Add(order);
        await db.SaveChangesAsync();
        db.Invoices.Add(InvoiceFor(customer.Id, 400m, InvoiceStatus.Sent, order.Id));
        await db.SaveChangesAsync();

        var exposure = await CustomerCreditExposure.ForCustomerAsync(db, customer.Id, CancellationToken.None);

        exposure.OpenArBalance.Should().Be(400m);
        exposure.PendingOrdersTotal.Should().Be(600m);
        exposure.TotalExposure.Should().Be(1_000m);
    }

    [Fact]
    public async Task Draft_invoice_stays_in_pending_and_out_of_open_ar()
    {
        using var db = TestDbContextFactory.Create();
        var customer = await SeedCustomerAsync(db);
        var order = Order(customer.Id, 1_000m);
        db.SalesOrders.Add(order);
        await db.SaveChangesAsync();
        db.Invoices.Add(InvoiceFor(customer.Id, 1_000m, InvoiceStatus.Draft, order.Id));
        await db.SaveChangesAsync();

        var exposure = await CustomerCreditExposure.ForCustomerAsync(db, customer.Id, CancellationToken.None);

        exposure.OpenArBalance.Should().Be(0m);
        exposure.OpenInvoiceCount.Should().Be(0);
        exposure.PendingOrdersTotal.Should().Be(1_000m);
    }

    [Fact]
    public async Task Paid_invoice_still_reduces_its_open_order_and_partial_payments_reduce_ar()
    {
        using var db = TestDbContextFactory.Create();
        var customer = await SeedCustomerAsync(db);
        var order = Order(customer.Id, 1_000m, SalesOrderStatus.Shipped);
        db.SalesOrders.Add(order);
        await db.SaveChangesAsync();
        db.Invoices.Add(InvoiceFor(customer.Id, 700m, InvoiceStatus.Paid, order.Id, paid: 700m));
        db.Invoices.Add(InvoiceFor(customer.Id, 500m, InvoiceStatus.PartiallyPaid, paid: 200m));
        await db.SaveChangesAsync();

        var exposure = await CustomerCreditExposure.ForCustomerAsync(db, customer.Id, CancellationToken.None);

        exposure.OpenArBalance.Should().Be(300m);
        exposure.OpenInvoiceCount.Should().Be(1);
        exposure.PendingOrdersTotal.Should().Be(300m);
    }

    [Fact]
    public async Task Over_invoiced_order_floors_pending_at_zero()
    {
        using var db = TestDbContextFactory.Create();
        var customer = await SeedCustomerAsync(db);
        var order = Order(customer.Id, 100m);
        db.SalesOrders.Add(order);
        await db.SaveChangesAsync();
        db.Invoices.Add(InvoiceFor(customer.Id, 150m, InvoiceStatus.Sent, order.Id));
        await db.SaveChangesAsync();

        var exposure = await CustomerCreditExposure.ForCustomerAsync(db, customer.Id, CancellationToken.None);

        exposure.PendingOrdersTotal.Should().Be(0m);
        exposure.OpenArBalance.Should().Be(150m);
    }

    [Fact]
    public async Task Credit_status_risk_report_and_service_agree()
    {
        using var db = TestDbContextFactory.Create();
        var customer = await SeedCustomerAsync(db, creditLimit: 2_000m);
        var order = Order(customer.Id, 1_000m);
        db.SalesOrders.Add(order);
        await db.SaveChangesAsync();
        db.Invoices.Add(InvoiceFor(customer.Id, 400m, InvoiceStatus.Sent, order.Id, paid: 100m));
        await db.SaveChangesAsync();

        var status = await new GetCreditStatusHandler(db)
            .Handle(new GetCreditStatusQuery(customer.Id), CancellationToken.None);
        var report = await new GetCreditRiskReportHandler(db)
            .Handle(new GetCreditRiskReportQuery(), CancellationToken.None);
        var service = await new CreditManagementService(db).GetCreditStatusAsync(customer.Id, CancellationToken.None);

        status.OpenArBalance.Should().Be(300m);
        status.PendingOrdersTotal.Should().Be(600m);
        status.TotalExposure.Should().Be(900m);
        report.Single(r => r.CustomerId == customer.Id).TotalExposure.Should().Be(900m);
        service.TotalExposure.Should().Be(900m);
    }

    [Fact]
    public async Task Summary_outstanding_nets_payments_and_ytd_revenue_is_pre_tax_invoiced_this_year()
    {
        using var db = TestDbContextFactory.Create();
        var customer = await SeedCustomerAsync(db);
        var thisYear = new DateTimeOffset(2026, 2, 10, 0, 0, 0, TimeSpan.Zero);
        var taxed = InvoiceFor(customer.Id, 1_000m, InvoiceStatus.PartiallyPaid, paid: 250m, invoiceDate: thisYear);
        taxed.TaxRate = 0.1m;
        db.Invoices.Add(taxed);
        db.Invoices.Add(InvoiceFor(customer.Id, 500m, InvoiceStatus.Draft, invoiceDate: thisYear));
        db.Invoices.Add(InvoiceFor(customer.Id, 800m, InvoiceStatus.Voided, invoiceDate: thisYear));
        db.Invoices.Add(InvoiceFor(customer.Id, 300m, InvoiceStatus.Paid, paid: 300m,
            invoiceDate: new DateTimeOffset(2025, 12, 31, 0, 0, 0, TimeSpan.Zero)));
        db.Invoices.Add(InvoiceFor(customer.Id, 400m, InvoiceStatus.Paid, paid: 400m,
            invoiceDate: new DateTimeOffset(2027, 1, 5, 0, 0, 0, TimeSpan.Zero)));
        await db.SaveChangesAsync();

        var clock = Mock.Of<IClock>(c => c.UtcNow == new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero));
        var summary = await new GetCustomerSummaryHandler(db, clock)
            .Handle(new GetCustomerSummaryQuery(customer.Id), CancellationToken.None);

        summary.OpenInvoiceTotal.Should().Be(850m);
        summary.OpenInvoiceCount.Should().Be(1);
        summary.YtdRevenue.Should().Be(1_000m);
    }
}
