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
}
