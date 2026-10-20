using FluentAssertions;
using Moq;

using Forge.Api.Features.Accounting;
using Forge.Api.Features.Invoices;
using Forge.Core.Entities;
using Forge.Core.Interfaces;

namespace Forge.Tests.Handlers.Invoices;

public class InvoiceAmountRoundingTests
{
    private static Invoice BuildInvoice(decimal taxRate, params (decimal Qty, decimal Price, bool Taxable)[] lines)
    {
        var invoice = new Invoice
        {
            Id = 42,
            InvoiceNumber = "INV-RND",
            TaxRate = taxRate,
            Customer = new Customer { Name = "Acme Corp" },
        };
        var n = 1;
        foreach (var (qty, price, taxable) in lines)
            invoice.Lines.Add(new InvoiceLine
            {
                Quantity = qty, UnitPrice = price, LineNumber = n++, Description = "line",
                TaxCode = taxable ? "TAX" : InvoiceLine.NonTaxableCode,
            });
        return invoice;
    }

    [Fact]
    public void Each_line_and_the_tax_round_to_the_cent()
    {
        var invoice = BuildInvoice(0.0725m, (3m, 33.335m, true), (0.1m, 0.2m, true));

        invoice.Subtotal.Should().Be(100.03m);
        invoice.Total.Should().Be(107.28m);
        invoice.TaxAmount.Should().Be(7.25m);
        (invoice.Subtotal + invoice.TaxAmount).Should().Be(invoice.Total);
    }

    [Fact]
    public void Subtotal_is_the_sum_of_the_rounded_lines()
    {
        var invoice = BuildInvoice(0.0725m, (1m, 33.333m, true), (1m, 33.333m, true), (1m, 33.333m, true));

        invoice.Lines.Should().OnlyContain(l => l.LineTotal == 33.33m);
        invoice.Subtotal.Should().Be(99.99m);
        invoice.TaxAmount.Should().Be(7.25m);
        invoice.Total.Should().Be(107.24m);
    }

    [Fact]
    public void Half_cent_rounds_away_from_zero()
    {
        var invoice = BuildInvoice(0m, (3m, 33.335m, true));

        invoice.Subtotal.Should().Be(100.01m);
        invoice.Total.Should().Be(100.01m);
        invoice.TaxAmount.Should().Be(0m);
    }

    [Fact]
    public void Paying_the_shown_total_leaves_a_zero_balance()
    {
        var invoice = BuildInvoice(0m, (3m, 33.335m, true));
        invoice.PaymentApplications.Add(new PaymentApplication { Amount = invoice.Total });

        invoice.BalanceDue.Should().Be(0m);
    }

    [Theory]
    [InlineData(0.0725, 3, 33.335)]
    [InlineData(0.0825, 7, 1.115)]
    [InlineData(0.06, 0.333, 10.01)]
    public void Document_amounts_match_the_posted_ar_amounts(decimal taxRate, decimal qty, decimal price)
    {
        var invoice = BuildInvoice(taxRate, (qty, price, true), (1m, 0.005m, false));

        var posted = InvoiceArPostingService.ComputePostedAmounts(invoice);

        posted.ArTotal.Should().Be(invoice.Total);
        posted.TaxAmount.Should().Be(invoice.TaxAmount);
        posted.Lines.Sum(l => l.Amount).Should().Be(invoice.Subtotal);
        posted.Lines.Should().OnlyContain(l => l.Amount == l.Line.LineTotal);
    }

    [Fact]
    public async Task Detail_returns_quantized_line_totals_and_the_canonical_balance()
    {
        var invoice = BuildInvoice(0m, (3m, 33.335m, true));
        invoice.PaymentApplications.Add(new PaymentApplication
        {
            Amount = 100.01m, Payment = new Payment { PaymentNumber = "PMT-1" },
        });
        var repo = new Mock<IInvoiceRepository>();
        repo.Setup(r => r.FindWithDetailsAsync(42, It.IsAny<CancellationToken>())).ReturnsAsync(invoice);

        var detail = await new GetInvoiceByIdHandler(repo.Object).Handle(new GetInvoiceByIdQuery(42), CancellationToken.None);

        detail.Lines.Single().LineTotal.Should().Be(100.01m);
        detail.Total.Should().Be(100.01m);
        detail.AmountPaid.Should().Be(100.01m);
        detail.BalanceDue.Should().Be(0m);
    }
}
