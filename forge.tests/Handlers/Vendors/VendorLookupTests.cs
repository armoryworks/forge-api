using System.Net;
using System.Net.Http.Json;

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

using Forge.Api.Features.Vendors;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Capabilities;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Vendors;

[Collection(CapabilityTestCollection.Name)]
public class VendorLookupTests(CapabilityTestWebApplicationFactory factory)
{
    private HttpClient Client(string role)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "1");
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    [Fact]
    public async Task Lookup_returns_active_matches_by_name_or_code_only()
    {
        using var db = TestDbContextFactory.Create();
        db.Vendors.AddRange(
            new Vendor { CompanyName = "Anodize Works", VendorNumber = "V-100", ContactName = "Pat", Email = "pat@example.test" },
            new Vendor { CompanyName = "Plating Partners", VendorNumber = "ANO-7" },
            new Vendor { CompanyName = "Anodize Retired", IsActive = false },
            new Vendor { CompanyName = "Heat Treat Co" });
        await db.SaveChangesAsync();

        var rows = await new GetVendorLookupHandler(db).Handle(new GetVendorLookupQuery("ano", 10), CancellationToken.None);

        rows.Select(r => r.Name).Should().Equal("Anodize Works", "Plating Partners");
        rows[0].Code.Should().Be("V-100");
        typeof(VendorLookupModel).GetProperties().Select(p => p.Name).Should().BeEquivalentTo(["Id", "Name", "Code"]);
    }

    [Fact]
    public async Task Lookup_caps_the_page_size()
    {
        using var db = TestDbContextFactory.Create();
        for (var i = 0; i < GetVendorLookupHandler.MaxLimit + 5; i++)
            db.Vendors.Add(new Vendor { CompanyName = $"Vendor {i:D3}" });
        await db.SaveChangesAsync();

        var rows = await new GetVendorLookupHandler(db).Handle(new GetVendorLookupQuery(null, 1000), CancellationToken.None);

        rows.Should().HaveCount(GetVendorLookupHandler.MaxLimit);
    }

    [Fact]
    public async Task An_engineer_can_look_up_vendors_but_not_list_the_vendor_master()
    {
        var name = $"Lookup {Guid.NewGuid():N}"[..20];
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Vendors.Add(new Vendor { CompanyName = name, VendorNumber = "LK-1" });
            await db.SaveChangesAsync();
        }

        var engineer = Client("Engineer");
        var lookup = await engineer.GetAsync($"/api/v1/vendors/lookup?q={Uri.EscapeDataString(name)}");
        var list = await engineer.GetAsync($"/api/v1/vendors?q={Uri.EscapeDataString(name)}");
        var worker = await Client("ProductionWorker").GetAsync($"/api/v1/vendors/lookup?q={Uri.EscapeDataString(name)}");

        lookup.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = await lookup.Content.ReadFromJsonAsync<List<VendorLookupModel>>();
        rows.Should().ContainSingle().Which.Should().Match<VendorLookupModel>(r => r.Name == name && r.Code == "LK-1");
        list.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        worker.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
