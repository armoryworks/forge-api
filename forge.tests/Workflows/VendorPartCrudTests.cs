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

namespace Forge.Tests.Workflows;

/// <summary>
/// Pillar 3 — Integration coverage for the VendorPart CRUD surface
/// (controller + handlers + validators). Lives next to the workflow tests
/// because they share the <see cref="CapabilityTestCollection"/> fixture
/// (in-memory DB seeded with capability catalog + workflow substrate).
/// </summary>
[Collection(CapabilityTestCollection.Name)]
public class VendorPartCrudTests(CapabilityTestWebApplicationFactory factory)
{
    private HttpClient AuthenticatedClient(string role = "Admin")
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "1");
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
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

    private async Task<int> SeedVendorAsync(string vendorName)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var vendor = new Vendor { CompanyName = vendorName };
        db.Vendors.Add(vendor);
        await db.SaveChangesAsync();
        return vendor.Id;
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

    private static CreateVendorPartRequestModel BuildCreateBody(
        int vendorId,
        int partId,
        bool isPreferred = false,
        bool isApproved = true,
        string? vendorPartNumber = null) =>
        new(
            VendorId: vendorId,
            PartId: partId,
            VendorPartNumber: vendorPartNumber,
            ManufacturerName: null,
            VendorMpn: null,
            LeadTimeDays: 14,
            MinOrderQty: 1m,
            PackSize: null,
            CountryOfOrigin: "US",
            HtsCode: null,
            IsApproved: isApproved,
            IsPreferred: isPreferred,
            Certifications: null,
            LastQuotedDate: null,
            Notes: null);

    [Fact]
    public async Task Create_NewVendorPart_201()
    {
        var client = AuthenticatedClient();
        var (vendorId, partId) = await SeedVendorAndPartAsync("Acme Co", "P-VP-001", "Widget");

        var body = BuildCreateBody(vendorId, partId, vendorPartNumber: "ACME-001");
        var resp = await client.PostAsJsonAsync("/api/v1/vendor-parts", body);

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var result = await resp.Content.ReadFromJsonAsync<VendorPartResponseModel>();
        result.Should().NotBeNull();
        result!.Id.Should().BePositive();
        result.VendorId.Should().Be(vendorId);
        result.PartId.Should().Be(partId);
        result.VendorCompanyName.Should().Be("Acme Co");
        result.PartNumber.Should().Be("P-VP-001");
        result.VendorPartNumber.Should().Be("ACME-001");
    }

    [Fact]
    public async Task Create_DuplicateVendorPart_409()
    {
        var client = AuthenticatedClient();
        var (vendorId, partId) = await SeedVendorAndPartAsync("Acme Dup", "P-VP-DUP", "WidgetDup");

        var body = BuildCreateBody(vendorId, partId);
        var first = await client.PostAsJsonAsync("/api/v1/vendor-parts", body);
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var second = await client.PostAsJsonAsync("/api/v1/vendor-parts", body);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Create_WithIsPreferredTrue_UnsetsOtherPreferred()
    {
        var client = AuthenticatedClient();
        var partId = await SeedPartAsync("P-VP-PREF", "PrefWidget");
        var vendorAId = await SeedVendorAsync("Vendor A");
        var vendorBId = await SeedVendorAsync("Vendor B");

        // First VendorPart for the part — preferred.
        var firstResp = await client.PostAsJsonAsync(
            "/api/v1/vendor-parts",
            BuildCreateBody(vendorAId, partId, isPreferred: true));
        firstResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var first = (await firstResp.Content.ReadFromJsonAsync<VendorPartResponseModel>())!;
        first.IsPreferred.Should().BeTrue();

        // Second — also preferred. The handler should clear the first's flag.
        var secondResp = await client.PostAsJsonAsync(
            "/api/v1/vendor-parts",
            BuildCreateBody(vendorBId, partId, isPreferred: true));
        secondResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var second = (await secondResp.Content.ReadFromJsonAsync<VendorPartResponseModel>())!;
        second.IsPreferred.Should().BeTrue();

        // Verify against the DB — only one preferred row remains.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var preferredRows = await db.VendorParts
            .Where(vp => vp.PartId == partId && vp.IsPreferred)
            .ToListAsync();
        preferredRows.Should().HaveCount(1);
        preferredRows[0].Id.Should().Be(second.Id);
    }

    [Fact]
    public async Task List_ByPart_ReturnsSortedByPreferredThenApprovedThenName()
    {
        var client = AuthenticatedClient();
        var partId = await SeedPartAsync("P-VP-SORT", "SortWidget");

        // Three vendors with intentionally non-alphabetic creation order so
        // we can detect a fallback to insertion order.
        var charlieId = await SeedVendorAsync("Charlie LLC");        // approved-only
        var alphaId = await SeedVendorAsync("Alpha Inc");            // preferred
        var bravoId = await SeedVendorAsync("Bravo Co");             // unapproved

        await client.PostAsJsonAsync(
            "/api/v1/vendor-parts",
            BuildCreateBody(charlieId, partId, isPreferred: false, isApproved: true));
        await client.PostAsJsonAsync(
            "/api/v1/vendor-parts",
            BuildCreateBody(alphaId, partId, isPreferred: true, isApproved: true));
        await client.PostAsJsonAsync(
            "/api/v1/vendor-parts",
            BuildCreateBody(bravoId, partId, isPreferred: false, isApproved: false));

        var rows = await client.GetFromJsonAsync<List<VendorPartResponseModel>>(
            $"/api/v1/parts/{partId}/vendor-parts");

        rows.Should().NotBeNull();
        rows!.Should().HaveCount(3);
        rows[0].VendorCompanyName.Should().Be("Alpha Inc",  "preferred wins");
        rows[1].VendorCompanyName.Should().Be("Charlie LLC", "approved beats unapproved");
        rows[2].VendorCompanyName.Should().Be("Bravo Co",    "unapproved is last");
    }

    [Fact]
    public async Task List_ByVendor_ReturnsSortedByPartNumber()
    {
        var client = AuthenticatedClient();
        var vendorId = await SeedVendorAsync("CatalogVendor");

        // Insert parts out of alphabetic order; list endpoint must sort by PartNumber.
        var midId = await SeedPartAsync("M-200", "Mid");
        var highId = await SeedPartAsync("Z-300", "High");
        var lowId = await SeedPartAsync("A-100", "Low");

        foreach (var pid in new[] { midId, highId, lowId })
        {
            var body = BuildCreateBody(vendorId, pid);
            (await client.PostAsJsonAsync("/api/v1/vendor-parts", body))
                .StatusCode.Should().Be(HttpStatusCode.Created);
        }

        var rows = await client.GetFromJsonAsync<List<VendorPartResponseModel>>(
            $"/api/v1/vendors/{vendorId}/vendor-parts");

        rows.Should().NotBeNull();
        rows!.Select(r => r.PartNumber).Should().ContainInOrder("A-100", "M-200", "Z-300");
    }

    [Fact]
    public async Task Create_WithIsManufacturer_DerivesMfrNameAndMirrorsPnMpn()
    {
        var client = AuthenticatedClient();
        var (vendorId, partId) = await SeedVendorAndPartAsync(
            "DirectMFR Corp", "P-VP-MFR", "OemWidget");

        var body = new CreateVendorPartRequestModel(
            VendorId: vendorId,
            PartId: partId,
            VendorPartNumber: "DM-9001",
            // Caller still might fill ManufacturerName from a UI auto-fill — the
            // handler must ignore it when IsManufacturer is true.
            ManufacturerName: "TYPO This Should Be Discarded",
            VendorMpn: null,
            LeadTimeDays: 14,
            MinOrderQty: null,
            PackSize: null,
            CountryOfOrigin: "US",
            HtsCode: null,
            IsApproved: true,
            IsPreferred: false,
            Certifications: null,
            LastQuotedDate: null,
            Notes: null,
            Currency: "USD",
            IsManufacturer: true);

        var resp = await client.PostAsJsonAsync("/api/v1/vendor-parts", body);
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var result = (await resp.Content.ReadFromJsonAsync<VendorPartResponseModel>())!;

        result.IsManufacturer.Should().BeTrue();
        result.ManufacturerName.Should().Be("DirectMFR Corp",
            "manufacturer name is derived from the vendor's company name on read");
        result.VendorPartNumber.Should().Be("DM-9001");
        result.VendorMpn.Should().Be("DM-9001",
            "with IsManufacturer=true the vendor's catalog # IS the MPN");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.VendorParts.AsNoTracking().FirstAsync(vp => vp.Id == result.Id);
        row.ManufacturerName.Should().BeNull(
            "the column is left null so vendor renames automatically propagate");
        row.VendorPartNumber.Should().Be("DM-9001");
        row.VendorMpn.Should().Be("DM-9001");
    }

    [Fact]
    public async Task Update_SettingIsManufacturer_CollapsesPnMpnAndClearsMfrName()
    {
        var client = AuthenticatedClient();
        var (vendorId, partId) = await SeedVendorAndPartAsync(
            "Becomes MFR", "P-VP-FLIP", "FlipWidget");

        // Start as a distributor row with distinct PN, MPN, and an explicit
        // ManufacturerName different from the vendor's name.
        var createBody = new CreateVendorPartRequestModel(
            VendorId: vendorId,
            PartId: partId,
            VendorPartNumber: "DIST-SKU",
            ManufacturerName: "SomeOtherMfr",
            VendorMpn: "OEM-123",
            LeadTimeDays: 14,
            MinOrderQty: null,
            PackSize: null,
            CountryOfOrigin: "US",
            HtsCode: null,
            IsApproved: true,
            IsPreferred: false,
            Certifications: null,
            LastQuotedDate: null,
            Notes: null,
            Currency: "USD",
            IsManufacturer: false);
        var createResp = await client.PostAsJsonAsync("/api/v1/vendor-parts", createBody);
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = (await createResp.Content.ReadFromJsonAsync<VendorPartResponseModel>())!;
        created.IsManufacturer.Should().BeFalse();
        created.ManufacturerName.Should().Be("SomeOtherMfr");

        // Flip IsManufacturer → true. The update handler must:
        //   1. Null out the stored ManufacturerName (read substitutes vendor)
        //   2. Mirror VendorPartNumber ↔ VendorMpn to a single identifier
        var updateBody = new UpdateVendorPartRequestModel(
            VendorPartNumber: null,                // leave alone — handler normalizes
            ManufacturerName: null,
            VendorMpn: null,
            LeadTimeDays: null,
            MinOrderQty: null,
            PackSize: null,
            CountryOfOrigin: null,
            HtsCode: null,
            IsApproved: true,
            IsPreferred: false,
            Certifications: null,
            LastQuotedDate: null,
            Notes: null,
            Currency: null,
            IsManufacturer: true);
        var updResp = await client.PutAsJsonAsync($"/api/v1/vendor-parts/{created.Id}", updateBody);
        updResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = (await updResp.Content.ReadFromJsonAsync<VendorPartResponseModel>())!;

        updated.IsManufacturer.Should().BeTrue();
        updated.ManufacturerName.Should().Be("Becomes MFR",
            "derived from the vendor's company name now that IsManufacturer is true");
        updated.VendorPartNumber.Should().Be("DIST-SKU");
        updated.VendorMpn.Should().Be("DIST-SKU",
            "MPN mirrors VendorPartNumber when IsManufacturer flips on");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.VendorParts.AsNoTracking().FirstAsync(vp => vp.Id == created.Id);
        row.ManufacturerName.Should().BeNull("stored column is null when derived");
    }

    [Fact]
    public async Task UpsertPriceTier_CreatesNewTier()
    {
        var client = AuthenticatedClient();
        var (vendorId, partId) = await SeedVendorAndPartAsync("PriceVendor", "P-VP-TIER", "TierWidget");

        var createResp = await client.PostAsJsonAsync(
            "/api/v1/vendor-parts",
            BuildCreateBody(vendorId, partId));
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var vp = (await createResp.Content.ReadFromJsonAsync<VendorPartResponseModel>())!;
        vp.PriceTiers.Should().BeEmpty();

        // Currency is no longer per-tier — lives on VendorPart, server snapshots.
        var tierBody = new UpsertVendorPartPriceTierRequestModel(
            MinQuantity: 100m,
            UnitPrice: 4.50m,
            EffectiveFrom: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            EffectiveTo: null,
            Notes: "Volume break");

        var tierResp = await client.PostAsJsonAsync(
            $"/api/v1/vendor-parts/{vp.Id}/price-tiers",
            tierBody);
        tierResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var tier = (await tierResp.Content.ReadFromJsonAsync<VendorPartPriceTierResponseModel>())!;
        tier.Id.Should().BePositive();
        tier.VendorPartId.Should().Be(vp.Id);
        tier.MinQuantity.Should().Be(100m);
        tier.UnitPrice.Should().Be(4.50m);

        // Confirm round-trip via the parent VendorPart read.
        var refreshed = await client.GetFromJsonAsync<VendorPartResponseModel>(
            $"/api/v1/vendor-parts/{vp.Id}");
        refreshed!.PriceTiers.Should().HaveCount(1);
        refreshed.PriceTiers[0].MinQuantity.Should().Be(100m);
        refreshed.PriceTiers[0].UnitPrice.Should().Be(4.50m);
    }
}
