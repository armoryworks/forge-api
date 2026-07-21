using System.Net;

using FluentAssertions;

using Forge.Tests.Capabilities;

namespace Forge.Tests.Remediation.Scheduling;

/// <summary>
/// Work-center read reachability. The controller previously restricted every action
/// (including GET) to Admin/Manager, so an Engineer-role identity got 403 on
/// GET /api/v1/work-centers. Defining a routing/operation is core engineering/MRP
/// work and requires reading the work centers operations route through, so the read
/// is now open to Engineer while writes stay Admin/Manager (mirrors the Quality
/// ECO/Gage method-level split). CAP-MD-WORKCENTERS is default-on.
/// </summary>
[Collection(CapabilityTestCollection.Name)]
public class WorkCentersReadReachabilityTests
{
    private readonly CapabilityTestWebApplicationFactory _factory;
    public WorkCentersReadReachabilityTests(CapabilityTestWebApplicationFactory factory) => _factory = factory;

    private HttpClient AuthClient(string role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "5");
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    [Fact] // negative->positive control: Engineer can now READ work centers (was 403)
    public async Task Engineer_can_read_work_centers()
    {
        var response = await AuthClient("Engineer").GetAsync("/api/v1/work-centers");
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "an Engineer defining routings/operations must be able to read the work centers they route through");
    }

    [Fact] // read reachability must not broaden write: Engineer still cannot create
    public async Task Engineer_cannot_create_a_work_center()
    {
        var response = await AuthClient("Engineer").PostAsync("/api/v1/work-centers", null);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "creating a work center stays Admin/Manager; only read was opened to Engineer");
    }
}
