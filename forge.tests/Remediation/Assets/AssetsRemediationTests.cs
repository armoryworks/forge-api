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

    [Fact] // AS-01 — GREEN: GET /api/v1/assets/{id} now exists (GetAssetByIdQuery).
    public async Task Single_asset_read_endpoint_returns_the_one_asset()
    {
        int assetId;
        using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var asset = new Asset { Name = "AS1-Single-Read", Status = AssetStatus.Active };
            db.Assets.Add(asset);
            await db.SaveChangesAsync();
            assetId = asset.Id;
        }

        var response = await AuthClient().GetAsync($"/api/v1/assets/{assetId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "opening an asset detail should fetch one asset by id, not the whole list");
        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("AS1-Single-Read", "the response must be that single asset's record");
    }

    [Fact] // AS-03 — GREEN: Retired is terminal; Retired→Active is rejected (409).
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
