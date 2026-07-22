using System.Net;
using System.Net.Http.Json;

using FluentAssertions;

using Forge.Tests.Capabilities;

namespace Forge.Tests.Remediation.Scheduling;

/// <summary>
/// Work-center authoring reachability. The controller previously restricted every action
/// (including GET) to Admin/Manager; B163 opened GET to Engineer. Defining a work center is
/// core engineering/MRP master-data authoring — an Engineer who builds routings/operations
/// also defines the work centers those operations run on — so CREATE and UPDATE are now open
/// to Engineer as well, mirroring how the Parts controller lets an Engineer author master-data
/// parts. DELETE stays Admin/Manager (destroying master data is heavier than defining it).
/// CAP-MD-WORKCENTERS is default-on, so this is purely the ASP.NET role gate.
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

    [Fact] // negative->positive control: Engineer can now CREATE a work center (was 403)
    public async Task Engineer_can_create_a_work_center()
    {
        var body = new
        {
            name = "Engineer-authored Cell",
            code = "ENG-WC-01",
            description = "Authored by an Engineer-role identity",
            dailyCapacityHours = 8m,
            efficiencyPercent = 100m,
            numberOfMachines = 1,
            laborCostPerHour = 25m,
            burdenRatePerHour = 10m,
            assetId = (int?)null,
            companyLocationId = (int?)null,
            sortOrder = 0,
        };

        var response = await AuthClient("Engineer").PostAsJsonAsync("/api/v1/work-centers", body);
        response.StatusCode.Should().Be(HttpStatusCode.Created,
            "defining a work center is core engineering master-data authoring, mirroring Engineer Part creation");
    }

    [Fact] // authoring grant must not broaden destroy: Engineer still cannot delete
    public async Task Engineer_cannot_delete_a_work_center()
    {
        var response = await AuthClient("Engineer").DeleteAsync("/api/v1/work-centers/1");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "deleting a work center stays Admin/Manager; only read + authoring were opened to Engineer");
    }
}
