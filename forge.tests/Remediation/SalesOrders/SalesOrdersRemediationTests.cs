using System.Net;

using FluentAssertions;

using Forge.Tests.Capabilities;

namespace Forge.Tests.Remediation.SalesOrders;

/// <summary>
/// Region 2 · Sales Orders RED test (see ../README.md). Finding BE-1 / SO-8: SO lines are
/// immutable after creation — no line edit/add/delete endpoint (the header PUT
/// /api/v1/orders/{id} does exist; the gap is lines + the missing UI caller, SO-8).
/// This asserts the line-edit endpoint exists (today absent → 404/405). CAP-O2C-SO on.
/// SO-4 (Draft orders absent from the Job-projected /sales-orders list) is deferred —
/// it depends on the read-projection shape and is tracked in the catalog.
/// </summary>
[Collection(CapabilityTestCollection.Name)]
public class SalesOrdersRemediationTests
{
    private readonly CapabilityTestWebApplicationFactory _factory;
    public SalesOrdersRemediationTests(CapabilityTestWebApplicationFactory factory) => _factory = factory;

    private HttpClient AuthClient(string role = "Admin")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "1");
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    [Fact(Skip = "RED: BE-1/SO-8 — sales-order lines are immutable (no line edit endpoint). " +
                 "Remove Skip when PUT /api/v1/orders/{id}/lines/{lineId} exists.")]
    public async Task SalesOrder_line_edit_endpoint_exists()
    {
        var response = await AuthClient().PutAsync("/api/v1/orders/1/lines/1", null);

        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
        response.StatusCode.Should().NotBe(HttpStatusCode.MethodNotAllowed,
            "draft sales-order lines must be editable, not frozen at creation");
    }
}
