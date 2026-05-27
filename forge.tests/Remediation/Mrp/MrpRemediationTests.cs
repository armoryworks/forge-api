using System.Net;
using System.Net.Http.Json;

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Capabilities;

namespace Forge.Tests.Remediation.Mrp;

/// <summary>
/// Region 3 · MRP RED test (see ../README.md). Finding MRP-03: ApplyForecastToMps has no
/// approval-state guard — a Draft forecast can be applied to the master schedule. Endpoints
/// sit behind CAP-PLAN-MRP (default OFF), so the test enables the cap first, seeds a Draft
/// forecast, and asserts applying it is rejected. G-38-MRP-1 (cap-OFF page freeze) is a UI
/// finding (Cypress), tracked in the catalog.
/// </summary>
[Collection(CapabilityTestCollection.Name)]
public class MrpRemediationTests
{
    private readonly CapabilityTestWebApplicationFactory _factory;
    public MrpRemediationTests(CapabilityTestWebApplicationFactory factory) => _factory = factory;

    private HttpClient AuthClient(string role = "Admin")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "1");
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    private IServiceScope NewScope() => _factory.Services.CreateScope();

    [Fact(Skip = "RED: MRP-03 — ApplyForecastToMps has no approval-state guard; a Draft forecast can " +
                 "be applied. Remove Skip when applying a non-Approved forecast is rejected (409).")]
    public async Task Applying_a_draft_forecast_to_the_MPS_is_rejected()
    {
        var admin = AuthClient();
        await admin.PutAsync("/api/v1/capabilities/CAP-PLAN-MRP/enabled",
            JsonContent.Create(new { enabled = true }));

        int forecastId;
        using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var forecast = new DemandForecast { Name = "MRP3-Draft", PartId = 1, Status = ForecastStatus.Draft };
            db.Add(forecast);
            await db.SaveChangesAsync();
            forecastId = forecast.Id;
        }

        var body = JsonContent.Create(new { masterScheduleId = 1 });
        var response = await admin.PostAsync($"/api/v1/mrp/forecasts/{forecastId}/apply", body);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "a Draft forecast must be Approved before it can be applied to the master schedule");
    }
}
