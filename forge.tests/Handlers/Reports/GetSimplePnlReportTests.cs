using FluentAssertions;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Reports;

public class GetSimplePnlReportTests
{
    [Fact]
    public async Task Sales_tax_collected_is_not_reported_as_revenue()
    {
        using var db = TestDbContextFactory.Create();
        db.Customers.Add(new Customer { Id = 1, Name = "Tax Co" });
        var invoice = new Invoice
        {
            InvoiceNumber = "INV-1",
            CustomerId = 1,
            Status = InvoiceStatus.Sent,
            TaxRate = 0.07m,
            InvoiceDate = new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.Zero),
            DueDate = new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.Zero),
        };
        invoice.Lines.Add(new InvoiceLine { Description = "Widget", Quantity = 10, UnitPrice = 100, LineNumber = 1 });
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        var rows = await new ReportRepository(db).GetSimplePnlAsync(
            new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 31, 23, 59, 59, TimeSpan.Zero),
            CancellationToken.None);

        rows.Where(r => r.Type == "Revenue").Sum(r => r.Amount).Should().Be(1000m);
        rows.Should().NotContain(r => r.Category == "Sales Tax Collected");
    }
    [Fact]
    public async Task Early_payment_discounts_taken_in_the_period_reduce_revenue()
    {
        using var db = TestDbContextFactory.Create();
        db.Customers.Add(new Customer { Id = 1, Name = "Terms Co" });
        var invoice = new Invoice
        {
            InvoiceNumber = "INV-2",
            CustomerId = 1,
            Status = InvoiceStatus.Paid,
            InvoiceDate = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
            DueDate = new DateTimeOffset(2026, 10, 31, 0, 0, 0, TimeSpan.Zero),
            DiscountPercent = 2m,
            DiscountDueDate = new DateTimeOffset(2026, 10, 11, 0, 0, 0, TimeSpan.Zero),
        };
        invoice.Lines.Add(new InvoiceLine { Description = "Widget", Quantity = 1, UnitPrice = 1000, LineNumber = 1 });
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();
        var payment = new Payment
        {
            PaymentNumber = "PMT-1",
            CustomerId = 1,
            Method = PaymentMethod.Check,
            Amount = 980m,
            PaymentDate = new DateTimeOffset(2026, 10, 8, 0, 0, 0, TimeSpan.Zero),
        };
        payment.Applications.Add(new PaymentApplication { InvoiceId = invoice.Id, Amount = 980m, DiscountAmount = 20m });
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        var rows = await new ReportRepository(db).GetSimplePnlAsync(
            new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 31, 23, 59, 59, TimeSpan.Zero),
            CancellationToken.None);

        rows.Should().Contain(r => r.Category == "Sales Discounts (payment basis)" && r.Amount == -20m);
        rows.Where(r => r.Type == "Revenue").Sum(r => r.Amount).Should().Be(980m);
    }
}
