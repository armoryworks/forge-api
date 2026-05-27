using System.Net;
using System.Net.Http.Json;

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Capabilities;

namespace Forge.Tests.Remediation.Quotes;

/// <summary>
/// Region 2 · Quotes RED test (see ../README.md). Finding BE-1 / Q-3: quote lines were
/// immutable. Now GREEN — PUT /quotes/{id}/lines/{lineId} edits a line, gated to Draft
/// (per the steer: edits only in Draft; originals preserved in history). CAP-O2C-QUOTE on.
/// </summary>
[Collection(CapabilityTestCollection.Name)]
public class QuotesLineEditRemediationTests
{
    private readonly CapabilityTestWebApplicationFactory _factory;
    public QuotesLineEditRemediationTests(CapabilityTestWebApplicationFactory factory) => _factory = factory;

    private HttpClient AuthClient(string role = "Admin")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "1");
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    private IServiceScope NewScope() => _factory.Services.CreateScope();

    private async Task<(int quoteId, int lineId)> SeedQuote(QuoteStatus status)
    {
        using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var customer = new Customer { Name = "BE1 Customer" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var quote = new Quote
        {
            CustomerId = customer.Id,
            Type = QuoteType.Quote,
            Status = status,
            Lines = { new QuoteLine { Description = "Original", Quantity = 1m, UnitPrice = 10m } },
        };
        db.Quotes.Add(quote);
        await db.SaveChangesAsync();
        return (quote.Id, quote.Lines.First().Id);
    }

    [Fact] // BE-1 GREEN — a draft quote line is editable
    public async Task Draft_quote_line_can_be_edited()
    {
        var (quoteId, lineId) = await SeedQuote(QuoteStatus.Draft);

        var body = JsonContent.Create(new { description = "Edited widget", quantity = 5m, unitPrice = 20m, notes = "rev" });
        var response = await AuthClient().PutAsync($"/api/v1/quotes/{quoteId}/lines/{lineId}", body);

        response.IsSuccessStatusCode.Should().BeTrue("draft quote lines must be editable");
        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("Edited widget", "the line edit must persist");
    }

    [Fact] // BE-1 GREEN — editing is blocked once the quote leaves Draft
    public async Task Non_draft_quote_line_edit_is_rejected()
    {
        var (quoteId, lineId) = await SeedQuote(QuoteStatus.Accepted);

        var body = JsonContent.Create(new { description = "Too late", quantity = 5m, unitPrice = 20m, notes = (string?)null });
        var response = await AuthClient().PutAsync($"/api/v1/quotes/{quoteId}/lines/{lineId}", body);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, "only draft quotes can have their lines edited");
    }
}
