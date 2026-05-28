using System.Net;
using System.Net.Http.Json;

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

using Forge.Data.Context;
using Forge.Tests.Capabilities;

namespace Forge.Tests.Remediation.Approvals;

/// <summary>
/// Region 4 · Approvals RED test (see ../README.md). Finding F-11-APPR-02: there was no
/// delete-workflow endpoint (GET/POST/PUT only), so stale approval workflows accumulated
/// with no way to retire them. Now GREEN — DELETE /api/v1/approvals/workflows/{id} soft-deletes.
/// CAP-P2P-APPROVALS is default-OFF, so the test enables it first. Deferred (catalog):
/// F-11-BE-01 (Manager approver stub), F-11-BE-02 (no notify on transition), F-12-AUDIT-01
/// (approval transitions not audited) — they need an approval flow seeded with a pending step.
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

    private IServiceScope NewScope() => _factory.Services.CreateScope();

    [Fact] // F-11-APPR-02 GREEN — a stale approval workflow can be retired (soft delete)
    public async Task Approval_workflow_can_be_retired()
    {
        var admin = AuthClient();
        await admin.PutAsync("/api/v1/capabilities/CAP-P2P-APPROVALS/enabled", JsonContent.Create(new { enabled = true }));

        int workflowId;
        using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var workflow = new ApprovalWorkflow { Name = "APPR2-Stale", EntityType = "PurchaseOrder" };
            db.ApprovalWorkflows.Add(workflow);
            await db.SaveChangesAsync();
            workflowId = workflow.Id;
        }

        var response = await admin.DeleteAsync($"/api/v1/approvals/workflows/{workflowId}");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent, "an admin must be able to retire a stale approval workflow");

        // The retired workflow must no longer surface in the list (soft-delete query filter).
        var list = await (await admin.GetAsync("/api/v1/approvals/workflows")).Content.ReadAsStringAsync();
        list.Should().NotContain("APPR2-Stale", "a retired workflow must drop out of the active list");
    }
}
