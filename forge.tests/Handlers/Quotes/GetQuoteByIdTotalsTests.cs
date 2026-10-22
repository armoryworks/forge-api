using FluentAssertions;
using Moq;

using Forge.Api.Features.Quotes;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Tests.Handlers.Quotes;

public class GetQuoteByIdTotalsTests
{
    private static Quote SubCentQuote(decimal taxRate) => new()
    {
        Id = 1,
        QuoteNumber = "Q-1",
        CustomerId = 1,
        Customer = new Customer { Id = 1, Name = "Acme" },
        TaxRate = taxRate,
        Lines =
        [
            new QuoteLine { Id = 1, LineNumber = 1, Description = "Eighths", Quantity = 3m, UnitPrice = 0.125m },
            new QuoteLine { Id = 2, LineNumber = 2, Description = "Half", Quantity = 0.5m, UnitPrice = 19.999m },
            new QuoteLine { Id = 3, LineNumber = 3, Description = "Whole", Quantity = 1m, UnitPrice = 10m },
        ],
    };

    private static async Task<QuoteDetailResponseModel> GetAsync(Quote quote)
    {
        var repo = new Mock<IQuoteRepository>();
        repo.Setup(r => r.FindWithDetailsAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(quote);
        return await new GetQuoteByIdHandler(repo.Object).Handle(new GetQuoteByIdQuery(1), CancellationToken.None);
    }

    [Fact]
    public async Task EachLineIsRoundedToTheCent_AndTheSubtotalIsTheirSum()
    {
        var quote = await GetAsync(SubCentQuote(0m));

        quote.Lines.Select(l => l.LineTotal).Should().Equal(0.38m, 10.00m, 10.00m);
        quote.Lines.Select(l => l.UnitPrice).Should().Equal(0.125m, 19.999m, 10m);
        quote.Subtotal.Should().Be(20.38m);
        quote.Subtotal.Should().Be(quote.Lines.Sum(l => l.LineTotal));
        quote.TaxAmount.Should().Be(0m);
        quote.Total.Should().Be(20.38m);
    }

    [Fact]
    public async Task Tax_IsTheRateOnTheRoundedLines_RoundedToTheCent()
    {
        var quote = await GetAsync(SubCentQuote(0.0725m));

        quote.Subtotal.Should().Be(20.38m);
        quote.TaxAmount.Should().Be(1.48m);
        quote.Total.Should().Be(21.86m);
        (quote.Subtotal + quote.TaxAmount).Should().Be(quote.Total);
    }

    [Fact]
    public void QuoteRoundsLikeTheInvoiceItBecomes()
    {
        var quote = SubCentQuote(0.0725m);
        var invoice = new Invoice
        {
            TaxRate = quote.TaxRate,
            Lines = quote.Lines
                .Select(l => new InvoiceLine { Description = l.Description, Quantity = l.Quantity, UnitPrice = l.UnitPrice })
                .ToList(),
        };

        quote.Lines.Select(l => l.LineTotal).Should().Equal(invoice.Lines.Select(l => l.LineTotal));
        quote.Subtotal.Should().Be(invoice.Subtotal);
        quote.TaxAmount.Should().Be(invoice.TaxAmount);
        quote.Total.Should().Be(invoice.Total);
    }
}
