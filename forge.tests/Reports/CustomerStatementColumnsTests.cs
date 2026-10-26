using FluentAssertions;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using UglyToad.PdfPig;

using Forge.Api.Features.Customers;
using Forge.Core.Entities;
using Forge.Core.Enums;

namespace Forge.Tests.Reports;

/// <summary>
/// The statement's invoice table splits what settled each invoice into Paid (cash) and Discounts / credits, so
/// Total − Paid − Discounts / credits = Balance on every row.
/// </summary>
public class CustomerStatementColumnsTests
{
    static CustomerStatementColumnsTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    private static Invoice Invoice(string number, decimal amount)
        => new()
        {
            InvoiceNumber = number,
            Status = InvoiceStatus.PartiallyPaid,
            InvoiceDate = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero),
            DueDate = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero),
            Lines = [new InvoiceLine { Description = "Brackets", Quantity = 1m, UnitPrice = amount, LineNumber = 1 }],
        };

    [Fact]
    public void A_discounted_and_a_credited_invoice_show_paid_discounts_credits_and_balance_that_add_up()
    {
        var discounted = Invoice("INV-D1", 1000m);
        discounted.PaymentApplications.Add(new PaymentApplication { Amount = 980m, DiscountAmount = 20m });
        discounted.Status = InvoiceStatus.Paid;

        var credited = Invoice("INV-C1", 500m);
        credited.PaymentApplications.Add(new PaymentApplication { Amount = 100m });
        credited.CreditApplications.Add(new CreditMemoApplication { Amount = 200m });

        var pdf = new CustomerStatementPdfDocument(
            new Customer { Name = "Statement Co" }, [discounted, credited], [], "Shop",
            new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero)).GeneratePdf();

        using var document = PdfDocument.Open(pdf);
        var text = string.Join(" ", document.GetPages().SelectMany(p => p.GetWords()).Select(w => w.Text));

        text.Should().Contain("Discounts /").And.Contain("credits");
        text.Should().Contain("$1,000.00 $980.00 $20.00 $0.00");
        text.Should().Contain("$500.00 $100.00 $200.00 $200.00");
    }
}
