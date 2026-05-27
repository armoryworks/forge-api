using System.Net.Http.Json;

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

using Forge.Core.Entities;
using Forge.Data.Context;
using Forge.Tests.Capabilities;

namespace Forge.Tests.Remediation.Vendors;

/// <summary>
/// Region 1 · Vendors RED tests (see ../README.md). Finding V9: the off-tier
/// variance % the user enters is silently dropped — UpdateVendorRequestModel and
/// VendorDetailResponseModel both omit OffTierVariancePct even though the column
/// exists on the Vendor entity. CAP-MD-VENDORS is default-on.
/// </summary>
[Collection(CapabilityTestCollection.Name)]
public class VendorsRemediationTests
{
    private readonly CapabilityTestWebApplicationFactory _factory;
    public VendorsRemediationTests(CapabilityTestWebApplicationFactory factory) => _factory = factory;

    private HttpClient AuthClient(string role = "Admin")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "1");
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    private IServiceScope NewScope() => _factory.Services.CreateScope();

    [Fact(Skip = "RED: V9 — off-tier variance % is dropped server-side (request + response models " +
                 "omit OffTierVariancePct). Remove Skip when the value round-trips through PUT then GET.")]
    public async Task Vendor_offTierVariancePct_round_trips_through_update()
    {
        int vendorId;
        using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var vendor = new Vendor { CompanyName = "V9-Variance" };
            db.Vendors.Add(vendor);
            await db.SaveChangesAsync();
            vendorId = vendor.Id;
        }

        var client = AuthClient();
        var put = await client.PutAsync($"/api/v1/vendors/{vendorId}",
            JsonContent.Create(new { companyName = "V9-Variance", offTierVariancePct = 12.5m }));
        put.IsSuccessStatusCode.Should().BeTrue("the update itself should succeed");

        var json = await (await client.GetAsync($"/api/v1/vendors/{vendorId}")).Content.ReadAsStringAsync();

        json.Should().Contain("offTierVariancePct",
            "the variance % must be exposed + persisted (today both request and response models drop it)");
        json.Should().Contain("12.5",
            "the exact value the user entered must round-trip, not fall back to the system default");
    }
}
