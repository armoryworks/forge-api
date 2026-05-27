using System.Net;

using FluentAssertions;

using Forge.Tests.Capabilities;

namespace Forge.Tests.Remediation.Kanban;

/// <summary>
/// Region 3 · Kanban/Jobs RED tests (see ../README.md). Ship-gate authz findings:
/// K-F13 (explode-bom), K-F15 (PUT job / reassign), K-F14 (dispose) are authGuard-only —
/// JobsController grants ProductionWorker at the controller level, so any worker can
/// explode BOMs into child jobs+reservations, reassign any job, or dispose (creating
/// Assets). These assert a low-priv role is rejected (403). CAP-MFG-WO-RELEASE is on.
/// Deferred (catalog): K-F3/K-F2 irreversible-guard, F-JQ1 advance-past-open-NCR (needs
/// stage setup).
/// </summary>
[Collection(CapabilityTestCollection.Name)]
public class JobsAuthzRemediationTests
{
    private readonly CapabilityTestWebApplicationFactory _factory;
    public JobsAuthzRemediationTests(CapabilityTestWebApplicationFactory factory) => _factory = factory;

    private HttpClient AuthClient(string role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "5");
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    [Fact(Skip = "RED: K-F13 — POST /jobs/{id}/explode-bom is authGuard-only; a ProductionWorker can " +
                 "create child jobs + reservations. Remove Skip when it requires Admin/Manager (403 otherwise).")]
    public async Task Production_worker_cannot_explode_bom()
    {
        var response = await AuthClient("ProductionWorker").PostAsync("/api/v1/jobs/1/explode-bom", null);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "exploding a BOM into child jobs/reservations must require Admin/Manager");
    }

    [Fact(Skip = "RED: K-F15 — PUT /jobs/{id} is authGuard-only; any user can reassign any job. " +
                 "Remove Skip when reassignment requires Admin/Manager (403 otherwise).")]
    public async Task Production_worker_cannot_reassign_a_job()
    {
        var response = await AuthClient("ProductionWorker").PutAsync("/api/v1/jobs/1", null);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "reassigning a job must require Admin/Manager, not any authenticated worker");
    }

    [Fact(Skip = "RED: K-F14 — POST /jobs/{id}/dispose is authGuard-only; it can capitalize an Asset. " +
                 "Remove Skip when disposition requires Admin/Manager (403 otherwise).")]
    public async Task Production_worker_cannot_dispose_a_job()
    {
        var response = await AuthClient("ProductionWorker").PostAsync("/api/v1/jobs/1/dispose", null);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "disposing a job (which can create an Asset) must require Admin/Manager");
    }
}
