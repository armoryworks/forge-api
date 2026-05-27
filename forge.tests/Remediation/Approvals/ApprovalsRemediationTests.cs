using System.Net;

using FluentAssertions;

using Forge.Tests.Capabilities;

namespace Forge.Tests.Remediation.Approvals;

/// <summary>
/// Region 4 · Approvals RED test (see ../README.md). Finding F-11-APPR-02: there is no
/// delete-workflow capability (GET/POST/PUT only), so stale approval workflows accumulate
/// with no way to retire them. This asserts the delete endpoint exists (route absent today
/// → 404). Deferred (catalog): F-11-BE-01 (Manager approver stub), F-11-BE-02 (no notify on
/// transition), F-12-AUDIT-01 (approval transitions not audited) — they need an approval
/// flow seeded with a pending step + approver.
/// </summary>
[Collection(CapabilityTestCollection.Name)]
public class ApprovalsRemediationTests
{
    private readonly CapabilityTestWebApplicationFactory _factory;
    public ApprovalsRemediationTests(CapabilityTestWebApplicationFactory factory) => _factory = factory;

    private HttpClient AuthClient(string role = "Admin")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "1");
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    [Fact(Skip = "RED: F-11-APPR-02 — approval workflows can't be deleted (no DELETE endpoint), so " +
                 "stale workflows accumulate. Remove Skip when DELETE /api/v1/approvals/workflows/{id} exists.")]
    public async Task Approval_workflow_delete_endpoint_exists()
    {
        var response = await AuthClient().DeleteAsync("/api/v1/approvals/workflows/1");

        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
        response.StatusCode.Should().NotBe(HttpStatusCode.MethodNotAllowed,
            "an admin must be able to retire a stale approval workflow");
    }
}
