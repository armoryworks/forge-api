using System.Net;
using System.Net.Http.Json;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Capabilities;

namespace Forge.Tests.Features.Parts;

/// <summary>
/// Dispatch C — Coverage for the PartPrice history endpoints
/// (GET / POST / DELETE /api/v1/parts/{id}/prices) plus the
/// VendorPartPriceTier history endpoint
/// (GET /api/v1/vendor-parts/{id}/price-tiers/history).
///
/// Lives next to the workflow tests because they share the
/// <see cref="CapabilityTestCollection"/> fixture.
/// </summary>
[Collection(CapabilityTestCollection.Name)]
public class PartPriceTests(CapabilityTestWebApplicationFactory factory)
{
    private HttpClient AuthenticatedClient(string role = "Admin")
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "1");
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    private async Task<int> SeedPartAsync(string partNumber, string partName)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var part = new Part
        {
            PartNumber = partNumber,
            Name = partName,
            ProcurementSource = ProcurementSource.Buy,
            InventoryClass = InventoryClass.Component,
            Status = PartStatus.Active,
        };
        db.Parts.Add(part);
        await db.SaveChangesAsync();
        return part.Id;
    }

    private async Task<(int VendorId, int PartId)> SeedVendorAndPartAsync(
        string vendorName, string partNumber, string partName)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var vendor = new Vendor { CompanyName = vendorName };
        var part = new Part
        {
            PartNumber = partNumber,
            Name = partName,
            ProcurementSource = ProcurementSource.Buy,
            InventoryClass = InventoryClass.Component,
            Status = PartStatus.Active,
        };
        db.Vendors.Add(vendor);
        db.Parts.Add(part);
        await db.SaveChangesAsync();
        return (vendor.Id, part.Id);
    }

    [Fact]
    public async Task Get_PartPrices_ReturnsHistoryDescByEffectiveFrom()
    {
        var client = AuthenticatedClient();
        var partId = await SeedPartAsync("PRC-HIST-001", "HistoryWidget");

        // Seed three rows directly so we control EffectiveFrom values.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = DateTimeOffset.UtcNow;
            db.PartPrices.AddRange(
                new PartPrice
                {
                    PartId = partId,
                    UnitPrice = 5.00m,
                    Currency = "USD",
                    EffectiveFrom = now.AddDays(-30),
                    EffectiveTo = now.AddDays(-10),
                    CreatedAt = now.AddDays(-30),
                    Notes = "initial",
                },
                new PartPrice
                {
                    PartId = partId,
                    UnitPrice = 6.00m,
                    Currency = "USD",
                    EffectiveFrom = now.AddDays(-10),
                    EffectiveTo = now.AddDays(-1),
                    CreatedAt = now.AddDays(-10),
                    Notes = "raw cost up",
                },
                new PartPrice
                {
                    PartId = partId,
                    UnitPrice = 7.00m,
                    Currency = "USD",
                    EffectiveFrom = now.AddDays(-1),
                    EffectiveTo = null,
                    CreatedAt = now.AddDays(-1),
                    Notes = "current",
                });
            await db.SaveChangesAsync();
        }

        var rows = await client.GetFromJsonAsync<List<PartPriceResponseModel>>(
            $"/api/v1/parts/{partId}/prices");

        rows.Should().NotBeNull();
        rows!.Should().HaveCount(3);
        rows.Select(r => r.UnitPrice).Should().ContainInOrder(7.00m, 6.00m, 5.00m);
        rows[0].EffectiveTo.Should().BeNull("the most recent row is the open one");
        rows[2].EffectiveTo.Should().NotBeNull("older rows are closed");
        rows.All(r => r.Currency == "USD").Should().BeTrue();
    }

    [Fact]
    public async Task Post_PartPrice_ClosesOutPreviousOpenRow()
    {
        var client = AuthenticatedClient();
        var partId = await SeedPartAsync("PRC-CLOSE-002", "CloseoutWidget");

        // First post — creates an open row.
        var firstBody = new AddPartPriceRequestModel(
            UnitPrice: 10.00m,
            Currency: "USD",
            EffectiveFrom: DateTimeOffset.UtcNow.AddDays(-5),
            Notes: "first");
        var firstResp = await client.PostAsJsonAsync($"/api/v1/parts/{partId}/prices", firstBody);
        firstResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var first = (await firstResp.Content.ReadFromJsonAsync<PartPriceResponseModel>())!;
        first.EffectiveTo.Should().BeNull();

        // Second post — should close out the first.
        var secondEffective = DateTimeOffset.UtcNow;
        var secondBody = new AddPartPriceRequestModel(
            UnitPrice: 12.50m,
            Currency: "USD",
            EffectiveFrom: secondEffective,
            Notes: "competitor matched");
        var secondResp = await client.PostAsJsonAsync($"/api/v1/parts/{partId}/prices", secondBody);
        secondResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var second = (await secondResp.Content.ReadFromJsonAsync<PartPriceResponseModel>())!;
        second.EffectiveTo.Should().BeNull();

        // Confirm first row was closed out at the second's EffectiveFrom.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var firstReloaded = await db.PartPrices.AsNoTracking().FirstAsync(p => p.Id == first.Id);
        firstReloaded.EffectiveTo.Should().NotBeNull();
        firstReloaded.EffectiveTo!.Value.Should().BeCloseTo(secondEffective.ToUniversalTime(), TimeSpan.FromSeconds(2));

        // Only the second row should be open.
        var openCount = await db.PartPrices.CountAsync(p => p.PartId == partId && p.EffectiveTo == null);
        openCount.Should().Be(1);
    }

    [Fact]
    public async Task Delete_PartPrice_RemovesHistoryRow()
    {
        var client = AuthenticatedClient();
        var partId = await SeedPartAsync("PRC-DEL-003", "DeleteWidget");

        var resp = await client.PostAsJsonAsync(
            $"/api/v1/parts/{partId}/prices",
            new AddPartPriceRequestModel(8.25m, "USD", DateTimeOffset.UtcNow, "to-delete"));
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var price = (await resp.Content.ReadFromJsonAsync<PartPriceResponseModel>())!;

        var delResp = await client.DeleteAsync($"/api/v1/parts/{partId}/prices/{price.Id}");
        delResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var rows = await client.GetFromJsonAsync<List<PartPriceResponseModel>>(
            $"/api/v1/parts/{partId}/prices");
        rows!.Should().NotContain(r => r.Id == price.Id);
    }

    [Fact]
    public async Task Get_VendorPartPriceTierHistory_ReturnsAllRowsOrdered()
    {
        var client = AuthenticatedClient();
        var (vendorId, partId) = await SeedVendorAndPartAsync(
            "TierVendor", "PRC-VPT-HIST", "TierHistoryWidget");

        // Create the VendorPart first.
        var createBody = new CreateVendorPartRequestModel(
            VendorId: vendorId,
            PartId: partId,
            VendorPartNumber: "TV-1",
            ManufacturerName: null,
            VendorMpn: null,
            LeadTimeDays: 7,
            MinOrderQty: 1m,
            PackSize: null,
            CountryOfOrigin: "US",
            HtsCode: null,
            IsApproved: true,
            IsPreferred: true,
            Certifications: null,
            LastQuotedDate: null,
            Notes: null);
        var vpResp = await client.PostAsJsonAsync("/api/v1/vendor-parts", createBody);
        vpResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var vp = (await vpResp.Content.ReadFromJsonAsync<VendorPartResponseModel>())!;

        // Seed three tiers: two old (closed) at one EffectiveFrom, one current.
        var oldEffective = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var oldClosed = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var newEffective = oldClosed; // new tier kicks in when old one expires

        async Task PostTier(decimal minQty, decimal unit, DateTimeOffset eff, DateTimeOffset? expiry, string note)
        {
            // Currency is no longer per-tier — it lives on VendorPart and the
            // server snapshots into the tier row at insert time.
            var body = new UpsertVendorPartPriceTierRequestModel(
                MinQuantity: minQty,
                UnitPrice: unit,
                EffectiveFrom: eff,
                EffectiveTo: expiry,
                Notes: note);
            var r = await client.PostAsJsonAsync($"/api/v1/vendor-parts/{vp.Id}/price-tiers", body);
            r.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        await PostTier(1, 5.00m, oldEffective, oldClosed, "old qty1");
        await PostTier(100, 4.00m, oldEffective, oldClosed, "old qty100");
        await PostTier(1, 6.00m, newEffective, null, "new qty1");

        var rows = await client.GetFromJsonAsync<List<VendorPartPriceTierResponseModel>>(
            $"/api/v1/vendor-parts/{vp.Id}/price-tiers/history");

        rows.Should().NotBeNull();
        rows!.Should().HaveCount(3);
        // Sorted by EffectiveFrom DESC, then MinQuantity ASC.
        rows[0].EffectiveFrom.Should().Be(newEffective);
        rows[0].MinQuantity.Should().Be(1m);
        rows[0].EffectiveTo.Should().BeNull();
        rows[1].EffectiveFrom.Should().Be(oldEffective);
        rows[1].MinQuantity.Should().Be(1m);
        rows[2].EffectiveFrom.Should().Be(oldEffective);
        rows[2].MinQuantity.Should().Be(100m);
    }

    [Fact]
    public async Task Get_VendorPartPriceTierHistory_UnknownVendorPart_404()
    {
        var client = AuthenticatedClient();
        var resp = await client.GetAsync("/api/v1/vendor-parts/999999/price-tiers/history");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
