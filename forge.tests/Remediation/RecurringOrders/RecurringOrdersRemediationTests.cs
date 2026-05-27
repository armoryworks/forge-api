using System.Net;

using FluentAssertions;

using Forge.Tests.Capabilities;

namespace Forge.Tests.Remediation.RecurringOrders;

/// <summary>
/// Region 2 · Recurring Orders RED test (see ../README.md). Finding BE-4: a recurring
/// order has no update endpoint (only GET/POST/DELETE) — editing means delete+recreate.
/// This asserts the update endpoint exists (today absent → 404/405). CAP-O2C-RECURRING is
/// default-OFF, so a not-404/405 assertion (route exists) holds regardless of the cap.
/// BE-5 (RecurringOrderJob uses non-sequential numbers + skips the cap check) is a
/// Hangfire-job behavior, tracked in the catalog.
/// </summary>
[Collection(CapabilityTestCollection.Name)]
public class RecurringOrdersRemediationTests
{
    private readonly CapabilityTestWebApplicationFactory _factory;
    public RecurringOrdersRemediationTests(CapabilityTestWebApplicationFactory factory) => _factory = factory;

    private HttpClient AuthClient(string role = "Admin")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "1");
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    [Fact(Skip = "RED: BE-4 — recurring orders have no update endpoint (delete+recreate only). " +
                 "Remove Skip when PUT /api/v1/recurring-orders/{id} exists.")]
    public async Task Recurring_order_update_endpoint_exists()
    {
        var response = await AuthClient().PutAsync("/api/v1/recurring-orders/1", null);

        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
        response.StatusCode.Should().NotBe(HttpStatusCode.MethodNotAllowed,
            "a recurring order should be editable in place, not delete-and-recreate");
    }
}
