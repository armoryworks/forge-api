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

    [Fact] // K-F13 GREEN — explode-bom now requires Admin/Manager
    public async Task Production_worker_cannot_explode_bom()
    {
        var response = await AuthClient("ProductionWorker").PostAsync("/api/v1/jobs/1/explode-bom", null);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "exploding a BOM into child jobs/reservations must require Admin/Manager");
    }

    [Fact] // K-F15 GREEN — PUT /jobs/{id} now requires Admin/Manager
    public async Task Production_worker_cannot_reassign_a_job()
    {
        var response = await AuthClient("ProductionWorker").PutAsync("/api/v1/jobs/1", null);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "reassigning a job must require Admin/Manager, not any authenticated worker");
    }

    [Fact] // K-F14 GREEN — dispose now requires Admin/Manager
    public async Task Production_worker_cannot_dispose_a_job()
    {
        var response = await AuthClient("ProductionWorker").PostAsync("/api/v1/jobs/1/dispose", null);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "disposing a job (which can create an Asset) must require Admin/Manager");
    }
}
