using System.Net;
using System.Net.Http.Json;

using FluentAssertions;

using Forge.Tests.Capabilities;

namespace Forge.Tests.Remediation.Planning;

/// <summary>
/// Region 3 · Planning Cycles RED test (see ../README.md). Ship-gate finding P-F6 /
/// G-38-MRP-3: PlanningCyclesController grants ProductionWorker at the controller level,
/// so a worker can create/activate/complete planning cycles. The endpoints sit behind
/// CAP-PLAN-MRP (default OFF), so the test enables the cap first (otherwise the 403 would
/// be the capability gate, not the role gate). Then it asserts a worker create is rejected.
/// </summary>
[Collection(CapabilityTestCollection.Name)]
public class PlanningRemediationTests
{
    private readonly CapabilityTestWebApplicationFactory _factory;
    public PlanningRemediationTests(CapabilityTestWebApplicationFactory factory) => _factory = factory;

    private HttpClient AuthClient(string role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "5");
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    [Fact(Skip = "RED: P-F6 / G-38-MRP-3 (ship-gate) — a ProductionWorker can create planning cycles " +
                 "(no per-endpoint role gate). Remove Skip when cycle mutations require Admin/Manager.")]
    public async Task Production_worker_cannot_create_a_planning_cycle()
    {
        // Precondition: CAP-PLAN-MRP is default-off, so enable it as Admin first; otherwise
        // the 403 would come from the capability gate rather than the role gate under test.
        await AuthClient("Admin").PutAsync("/api/v1/capabilities/CAP-PLAN-MRP/enabled",
            JsonContent.Create(new { enabled = true }));

        var body = JsonContent.Create(new
        {
            name = "PF6-Cycle",
            startDate = DateTimeOffset.UtcNow,
            endDate = DateTimeOffset.UtcNow.AddDays(14),
            durationDays = 14,
        });
        var response = await AuthClient("ProductionWorker").PostAsync("/api/v1/planning-cycles", body);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "creating/activating/completing a planning cycle must require Admin/Manager");
    }
}
