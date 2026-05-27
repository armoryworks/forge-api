using System.Net;
using System.Net.Http.Json;

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Capabilities;

namespace Forge.Tests.Remediation.Assets;

/// <summary>
/// Region 3 · Assets RED tests (see ../README.md).
/// Findings: AS-01 (no single-asset read endpoint — detail surfaces fetch the whole list
/// and find), AS-03 (status PATCH has no state-machine guard — any→any, e.g. Retired→Active).
/// CAP-MD-ASSETS is default-on (Admin/Manager/Engineer). AS-02 (PM-schedule UI) is UI, in catalog.
/// </summary>
[Collection(CapabilityTestCollection.Name)]
public class AssetsRemediationTests
{
    private readonly CapabilityTestWebApplicationFactory _factory;
    public AssetsRemediationTests(CapabilityTestWebApplicationFactory factory) => _factory = factory;

    private HttpClient AuthClient(string role = "Admin")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "1");
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    private IServiceScope NewScope() => _factory.Services.CreateScope();

    [Fact(Skip = "RED: AS-01 — there is no GET /assets/{id} single-read endpoint (detail fetches the " +
                 "full list and finds). Remove Skip when GET /api/v1/assets/{id} exists.")]
    public async Task Single_asset_read_endpoint_exists()
    {
        var response = await AuthClient().GetAsync("/api/v1/assets/1");

        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
        response.StatusCode.Should().NotBe(HttpStatusCode.MethodNotAllowed,
            "opening an asset detail should fetch one asset, not the whole list");
    }

    [Fact(Skip = "RED: AS-03 — asset status PATCH has no state-machine guard (any→any). " +
                 "Remove Skip when an invalid transition (Retired→Active) is rejected (409).")]
    public async Task Reactivating_a_retired_asset_is_rejected()
    {
        int assetId;
        using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var asset = new Asset { Name = "AS3-Retired", Status = AssetStatus.Retired };
            db.Assets.Add(asset);
            await db.SaveChangesAsync();
            assetId = asset.Id;
        }

        var body = JsonContent.Create(new { status = "Active" });
        var response = await AuthClient().PatchAsync($"/api/v1/assets/{assetId}", body);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "a Retired asset must not silently transition back to Active");
    }
}
