using System.Net.Http.Json;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Forge.Data.Context;
using Forge.Tests.Capabilities;

namespace Forge.Tests.Remediation.Quotes;

/// <summary>
/// AUDIT-19-S1: customer price lists were a dead input to quote-line pricing. Now a catalog-part line
/// created without an explicit price (0) resolves its unit price from the customer's active price
/// list. CAP-O2C-QUOTE is default-on.
/// </summary>
[Collection(CapabilityTestCollection.Name)]
public class QuotesPricingRemediationTests
{
    private readonly CapabilityTestWebApplicationFactory _factory;
    public QuotesPricingRemediationTests(CapabilityTestWebApplicationFactory factory) => _factory = factory;

    private HttpClient AuthClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "1");
        client.DefaultRequestHeaders.Add("X-Test-Role", "Admin");
        return client;
    }

    private IServiceScope NewScope() => _factory.Services.CreateScope();

    [Fact]
    public async Task Quote_line_with_no_price_resolves_from_customer_price_list()
    {
        int customerId, partId;
        using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var customer = new Customer { Name = "AUDIT19 Customer" };
            var part = new Part { PartNumber = $"P-PL-{Guid.NewGuid().ToString("N")[..8]}", Name = "Listed Part" };
            db.Customers.Add(customer);
            db.Parts.Add(part);
            await db.SaveChangesAsync();

            var priceList = new PriceList { Name = "Customer List", CustomerId = customer.Id, IsActive = true };
            priceList.Entries.Add(new PriceListEntry { PartId = part.Id, UnitPrice = 42m });
            db.PriceLists.Add(priceList);
            await db.SaveChangesAsync();

            customerId = customer.Id;
            partId = part.Id;
        }

        // Create a quote line for the listed part with NO price (0) — the server should fill 42 from the list.
        var body = JsonContent.Create(new
        {
            customerId,
            taxRate = 0m,
            lines = new[] { new { partId, description = "Listed Part", quantity = 2m, unitPrice = 0m } },
        });
        (await AuthClient().PostAsync("/api/v1/quotes", body)).IsSuccessStatusCode.Should().BeTrue();

        using var verify = NewScope();
        var db2 = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        var line = await db2.QuoteLines.FirstAsync(l => l.PartId == partId);
        line.UnitPrice.Should().Be(42m, "the quote line price must resolve from the customer's price list (AUDIT-19-S1)");
    }
}
