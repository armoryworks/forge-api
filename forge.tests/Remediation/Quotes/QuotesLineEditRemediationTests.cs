using System.Net;

using FluentAssertions;

using Forge.Tests.Capabilities;

namespace Forge.Tests.Remediation.Quotes;

/// <summary>
/// Region 2 · Quotes RED test (see ../README.md). Finding BE-1 / Q-3: quote lines are
/// immutable after creation — there is no line edit/add/delete endpoint. This asserts
/// the line-edit endpoint exists (today the route is absent → 404/405). CAP-O2C-QUOTE
/// is default-on. (The convert-side findings AUDIT-S3/S4 are covered by
/// ConvertQuoteToOrderRemediationTests; AUDIT-19-S1 price-list pricing is deferred —
/// it needs price-list seeding and is tracked in the catalog.)
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

    [Fact(Skip = "RED: BE-1/Q-3 — quote lines are immutable (no line edit endpoint). " +
                 "Remove Skip when PUT /api/v1/quotes/{id}/lines/{lineId} exists.")]
    public async Task Quote_line_edit_endpoint_exists()
    {
        var response = await AuthClient().PutAsync("/api/v1/quotes/1/lines/1", null);

        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
        response.StatusCode.Should().NotBe(HttpStatusCode.MethodNotAllowed,
            "draft quotes must be editable line-by-line, not frozen at creation");
    }
}
