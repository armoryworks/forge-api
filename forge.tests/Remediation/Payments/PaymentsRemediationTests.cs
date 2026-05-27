using System.Net;

using FluentAssertions;

using Forge.Tests.Capabilities;

namespace Forge.Tests.Remediation.Payments;

/// <summary>
/// Region 2 · Payments RED tests (see ../README.md). Finding P06-5: payments are
/// delete-only — there is no void/reversal and no amend (update) path, so a corrected
/// payment leaves no audit trail. These assert the void + amend endpoints exist (today
/// both routes are absent → 404/405). CAP-O2C-CASH is default-on.
/// </summary>
[Collection(CapabilityTestCollection.Name)]
public class PaymentsRemediationTests
{
    private readonly CapabilityTestWebApplicationFactory _factory;
    public PaymentsRemediationTests(CapabilityTestWebApplicationFactory factory) => _factory = factory;

    private HttpClient AuthClient(string role = "Admin")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "1");
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    [Fact(Skip = "RED: P06-5 — no payment void/reversal path. " +
                 "Remove Skip when POST /api/v1/payments/{id}/void exists.")]
    public async Task Payment_void_endpoint_exists()
    {
        var response = await AuthClient().PostAsync("/api/v1/payments/1/void", null);

        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
        response.StatusCode.Should().NotBe(HttpStatusCode.MethodNotAllowed,
            "a mistaken payment must be reversible with an audit trail, not just hard-deleted");
    }

    [Fact(Skip = "RED: P06-5 — no payment amend (update) path. " +
                 "Remove Skip when PUT /api/v1/payments/{id} exists.")]
    public async Task Payment_amend_endpoint_exists()
    {
        var response = await AuthClient().PutAsync("/api/v1/payments/1", null);

        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
        response.StatusCode.Should().NotBe(HttpStatusCode.MethodNotAllowed,
            "amount/method/date/reference corrections need an amend path, not delete+recreate");
    }
}
