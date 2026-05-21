using System.Net.Http.Json;

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Capabilities;

namespace Forge.Tests.Integration;

/// <summary>
/// F-041 regression: quote list endpoint must return tax-inclusive Total,
/// matching the detail endpoint. Pre-fix, list returned the pre-tax subtotal.
/// </summary>
[Collection(CapabilityTestCollection.Name)]
public class F041QuoteTotalAlignmentTests(CapabilityTestWebApplicationFactory factory)
{
    private HttpClient AuthClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "1");
        client.DefaultRequestHeaders.Add("X-Test-Role", "Admin");
        return client;
    }

    [Fact]
    public async Task QuoteList_Total_IsTaxInclusive_MatchingDetail()
    {
        // line 1: qty=10 * $5.00 = $50; line 2: qty=2 * $25.00 = $50 → subtotal $100
        const decimal taxRate = 0.10m;
        const decimal expectedSubtotal = 100m;
        const decimal expectedTotal = 110m; // 100 * (1 + 0.10)

        int customerId;
        int quoteId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var customer = new Customer { Name = $"F041-{Guid.NewGuid():N}" };
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
            customerId = customer.Id;

            var quote = new Quote
            {
                Type = QuoteType.Quote,
                CustomerId = customerId,
                QuoteNumber = $"QT-F041-{customerId}",
                TaxRate = taxRate,
                Status = QuoteStatus.Draft,
            };
            db.Quotes.Add(quote);
            await db.SaveChangesAsync();
            quoteId = quote.Id;

            db.QuoteLines.AddRange(
                new QuoteLine { QuoteId = quoteId, Description = "Widget A", Quantity = 10m, UnitPrice = 5.00m, LineNumber = 1 },
                new QuoteLine { QuoteId = quoteId, Description = "Widget B", Quantity = 2m, UnitPrice = 25.00m, LineNumber = 2 });
            await db.SaveChangesAsync();
        }

        var client = AuthClient();

        var listItems = await client.GetFromJsonAsync<List<QuoteListItemModel>>(
            $"/api/v1/quotes?customerId={customerId}");
        var detail = await client.GetFromJsonAsync<QuoteDetailResponseModel>(
            $"/api/v1/quotes/{quoteId}");

        listItems.Should().NotBeNull().And.ContainSingle();
        var listItem = listItems![0];

        // list Total must be tax-inclusive (the F-041 fix)
        listItem.Total.Should().Be(expectedTotal,
            "list Total must be subtotal*(1+taxRate), not the pre-tax subtotal");
        listItem.Total.Should().NotBe(expectedSubtotal,
            "pre-fix regression: list was returning the raw subtotal");

        // list Total must agree with detail Total
        detail.Should().NotBeNull();
        detail!.Total.Should().Be(listItem.Total,
            "list and detail endpoints must return the same tax-inclusive Total");
        detail.Subtotal.Should().Be(expectedSubtotal);
        detail.TaxAmount.Should().Be(expectedSubtotal * taxRate);
    }
}
