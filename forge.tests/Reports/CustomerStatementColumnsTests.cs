using System.Net;
using System.Text.RegularExpressions;

using AwesomeAssertions;

using Forge.Api.Features.Customers;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Tests.Reports;

/// <summary>
/// The statement's invoice table splits what settled each invoice into Paid (cash) and Discounts / credits, so
/// Total − Paid − Discounts / credits = Balance on every row.
/// </summary>
public class CustomerStatementColumnsTests
{
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

        var html = CustomerStatementPdfDocument.Build(
            new Customer { Name = "Statement Co" }, [discounted, credited], [], "Shop",
            new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero)).Html;

        var text = Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", " ")), @"\s+", " ");

        text.Should().Contain("Discounts /").And.Contain("credits");
        text.Should().Contain("$1,000.00 $980.00 $20.00 $0.00");
        text.Should().Contain("$500.00 $100.00 $200.00 $200.00");
    }

    private static List<HtmlPdfDocument> SplitStatement(int invoices, int payments, int cellsPerPart)
        => CustomerStatementPdfDocument.BuildParts(
            new Customer { Name = "Statement Co" },
            Enumerable.Range(1, invoices).Select(i => Invoice($"INV-{i:000}", 10m * i)).ToList(),
            Enumerable.Range(1, payments)
                .Select(i => new Payment { Id = i, PaymentNumber = $"PMT-{i:000}", Amount = i, PaymentDate = new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero) })
                .ToList(),
            "Shop",
            new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero),
            cellsPerPart: cellsPerPart).ToList();

    [Fact]
    public void A_long_statement_splits_into_parts_that_list_every_row_once_and_total_on_the_last()
    {
        var parts = SplitStatement(invoices: 5, payments: 3, cellsPerPart: 24);

        parts.Should().HaveCount(3);
        parts[0].Html.Should().Contain("Part 1 of 3").And.Contain(">Invoices<").And.NotContain("Payment History")
            .And.NotContain("Total Balance Due");
        parts[1].Html.Should().Contain("Part 2 of 3").And.Contain(">Invoices (continued)<").And.Contain(">Payment History<")
            .And.NotContain("Total Balance Due");
        parts[2].Html.Should().Contain("Part 3 of 3").And.Contain(">Payment History (continued)<").And.Contain("Total Balance Due")
            .And.Contain("<div class=\"page-footer\">Statement generated October 15, 2026 &#183; Part 3 of 3 &middot; Page ");

        var all = string.Concat(parts.Select(p => p.Html));
        foreach (var number in Enumerable.Range(1, 5).Select(i => $">INV-{i:000}<").Concat(Enumerable.Range(1, 3).Select(i => $">PMT-{i:000}<")))
            Regex.Matches(all, Regex.Escape(number)).Should().ContainSingle(number);
        parts.Should().OnlyContain(p => p.Html.Contains("CUSTOMER STATEMENT") && p.Html.Contains("Statement Co"));
    }

    [Fact]
    public void A_statement_that_fits_is_one_part_identical_to_the_whole_document()
    {
        var part = SplitStatement(invoices: 5, payments: 3, cellsPerPart: 6000).Should().ContainSingle().Subject;

        part.Html.Should().NotContain("Part 1").And.NotContain("(continued)");
        part.Html.Should().Be(CustomerStatementPdfDocument.Build(
            new Customer { Name = "Statement Co" },
            Enumerable.Range(1, 5).Select(i => Invoice($"INV-{i:000}", 10m * i)).ToList(),
            Enumerable.Range(1, 3)
                .Select(i => new Payment { Id = i, PaymentNumber = $"PMT-{i:000}", Amount = i, PaymentDate = new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero) })
                .ToList(),
            "Shop",
            new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero)).Html);
    }

    [Fact]
    public void A_statement_with_no_invoices_still_shows_the_invoice_table()
    {
        var part = SplitStatement(invoices: 0, payments: 2, cellsPerPart: 6000).Should().ContainSingle().Subject;

        part.Html.Should().Contain(">Invoices<").And.Contain("<th>Invoice #</th>").And.Contain(">Payment History<");
    }
}
