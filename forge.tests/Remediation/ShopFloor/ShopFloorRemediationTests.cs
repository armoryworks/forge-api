using System.Net;

using FluentAssertions;

using Forge.Tests.Capabilities;

namespace Forge.Tests.Remediation.ShopFloor;

/// <summary>
/// Region 3 · Shop Floor RED tests (see ../README.md). Ship-gate authz findings:
/// SF-04 (complete-job) and SF-05 (assign-job) are class-[Authorize] only (any
/// authenticated role) — complete-job jumps to the final irreversible stage and
/// assign-job lets anyone steal any job. These assert a ProductionWorker is rejected
/// (403). CAP-MFG-SHOPFLOOR is on. SF-10 (clock — AllowAnonymous+KioskTerminalAuth, no
/// role evaluated) needs a PIN/JWT-tether fix, not a role-403; tracked in the catalog.
/// </summary>
[Collection(CapabilityTestCollection.Name)]
public class ShopFloorRemediationTests
{
    private readonly CapabilityTestWebApplicationFactory _factory;
    public ShopFloorRemediationTests(CapabilityTestWebApplicationFactory factory) => _factory = factory;

    private HttpClient AuthClient(string role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "5");
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    [Fact] // SF-04 GREEN — complete-job now requires Admin/Manager
    public async Task Production_worker_cannot_complete_a_job_from_the_kiosk()
    {
        var response = await AuthClient("ProductionWorker")
            .PostAsync("/api/v1/display/shop-floor/complete-job", null);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "completing a job (irreversible) must require Admin/Manager / supervisor approval");
    }

    [Fact] // SF-05 GREEN — assign-job now requires Admin/Manager
    public async Task Production_worker_cannot_assign_a_job_from_the_kiosk()
    {
        var response = await AuthClient("ProductionWorker")
            .PostAsync("/api/v1/display/shop-floor/assign-job", null);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "assigning/stealing a job must require Admin/Manager, not any authenticated user");
    }
}
