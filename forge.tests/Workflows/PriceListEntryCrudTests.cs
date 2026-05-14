using System.Net;
using System.Net.Http.Json;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Forge.Api.Capabilities;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Capabilities;

namespace Forge.Tests.Workflows;

/// <summary>
/// Integration coverage for the PriceListEntry CRUD surface
/// (controller + handlers + validators) added for the customer Pricing tab UI.
///
/// Pattern matches <see cref="VendorPartCrudTests"/> — same in-memory DB
/// fixture (<see cref="CapabilityTestCollection"/>), same test-user header
/// flow, and same dispatch-style "create + read back / update / delete /
/// list with search / pagination" coverage set.
/// </summary>
[Collection(CapabilityTestCollection.Name)]
public class PriceListEntryCrudTests(CapabilityTestWebApplicationFactory factory)
{
    private HttpClient AuthenticatedClient(string role = "Admin")
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "1");
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    private async Task EnablePriceListCapabilityAsync()
    {
        // CAP-MD-PRICELIST is IsDefaultOn=false in the catalog; the gate
        // middleware will 403 every endpoint until it's flipped on.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Capabilities.FirstAsync(c => c.Code == "CAP-MD-PRICELIST");
        if (!row.Enabled)
        {
            row.Enabled = true;
            await db.SaveChangesAsync();
            var snapshots = scope.ServiceProvider.GetRequiredService<ICapabilitySnapshotProvider>();
            await snapshots.RefreshAsync();
        }
    }

    private async Task<int> SeedPartAsync(string partNumber, string name)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var part = new Part
        {
            PartNumber = partNumber,
            Name = name,
            ProcurementSource = ProcurementSource.Buy,
            InventoryClass = InventoryClass.Component,
            Status = PartStatus.Active,
        };
        db.Parts.Add(part);
        await db.SaveChangesAsync();
        return part.Id;
    }

    private async Task<int> SeedPriceListAsync(string name = "PL-Test")
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pl = new PriceList { Name = name, IsDefault = false, IsActive = true };
        db.PriceLists.Add(pl);
        await db.SaveChangesAsync();
        return pl.Id;
    }

    private static CreatePriceListEntryRequestModel BuildBody(
        int partId, decimal unitPrice = 12.50m, int minQty = 1,
        string currency = "USD", string? notes = null) =>
        new(PartId: partId, UnitPrice: unitPrice, MinQuantity: minQty,
            Currency: currency, Notes: notes);

    [Fact]
    public async Task Create_NewEntry_Returns201_AndReadsBack()
    {
        await EnablePriceListCapabilityAsync();
        var client = AuthenticatedClient();
        var partId = await SeedPartAsync($"P-PLE-{Guid.NewGuid():N}".Substring(0, 12), "Test Part");
        var listId = await SeedPriceListAsync($"PL-Create-{Guid.NewGuid():N}");

        var resp = await client.PostAsJsonAsync(
            $"/api/v1/price-lists/{listId}/entries",
            BuildBody(partId, unitPrice: 14.25m, minQty: 100, notes: "Bulk break"));

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await resp.Content.ReadFromJsonAsync<PriceListEntryResponseModel>();
        created.Should().NotBeNull();
        created!.Id.Should().BePositive();
        created.PriceListId.Should().Be(listId);
        created.PartId.Should().Be(partId);
        created.UnitPrice.Should().Be(14.25m);
        created.MinQuantity.Should().Be(100);
        created.Currency.Should().Be("USD");
        created.Notes.Should().Be("Bulk break");

        // Read it back via the flat GET endpoint.
        var read = await client.GetFromJsonAsync<PriceListEntryResponseModel>(
            $"/api/v1/price-list-entries/{created.Id}");
        read!.Id.Should().Be(created.Id);
        read.UnitPrice.Should().Be(14.25m);
        read.MinQuantity.Should().Be(100);
    }

    [Fact]
    public async Task Update_ExistingEntry_PersistsChanges()
    {
        await EnablePriceListCapabilityAsync();
        var client = AuthenticatedClient();
        var partId = await SeedPartAsync($"P-PLE-{Guid.NewGuid():N}".Substring(0, 12), "Update Part");
        var listId = await SeedPriceListAsync($"PL-Update-{Guid.NewGuid():N}");

        var createResp = await client.PostAsJsonAsync(
            $"/api/v1/price-lists/{listId}/entries", BuildBody(partId));
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = (await createResp.Content.ReadFromJsonAsync<PriceListEntryResponseModel>())!;

        var update = new UpdatePriceListEntryRequestModel(
            UnitPrice: 9.99m, MinQuantity: 50, Currency: "EUR", Notes: "Negotiated 2026-04");

        var updateResp = await client.PutAsJsonAsync(
            $"/api/v1/price-list-entries/{created.Id}", update);
        updateResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var refreshed = await client.GetFromJsonAsync<PriceListEntryResponseModel>(
            $"/api/v1/price-list-entries/{created.Id}");
        refreshed!.UnitPrice.Should().Be(9.99m);
        refreshed.MinQuantity.Should().Be(50);
        refreshed.Currency.Should().Be("EUR");
        refreshed.Notes.Should().Be("Negotiated 2026-04");
        refreshed.PartId.Should().Be(partId, "PartId is immutable on update");
    }

    [Fact]
    public async Task Delete_RemovesEntry_FromListResponse()
    {
        await EnablePriceListCapabilityAsync();
        var client = AuthenticatedClient();
        var partId = await SeedPartAsync($"P-PLE-{Guid.NewGuid():N}".Substring(0, 12), "Delete Part");
        var listId = await SeedPriceListAsync($"PL-Delete-{Guid.NewGuid():N}");

        var createResp = await client.PostAsJsonAsync(
            $"/api/v1/price-lists/{listId}/entries", BuildBody(partId));
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = (await createResp.Content.ReadFromJsonAsync<PriceListEntryResponseModel>())!;

        var deleteResp = await client.DeleteAsync($"/api/v1/price-list-entries/{created.Id}");
        deleteResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var listResp = await client.GetFromJsonAsync<PagedResponse<PriceListEntryResponseModel>>(
            $"/api/v1/price-lists/{listId}/entries");
        listResp!.Items.Should().NotContain(e => e.Id == created.Id);
        listResp.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task List_WithSearch_FiltersByPartNumberOrName()
    {
        await EnablePriceListCapabilityAsync();
        var client = AuthenticatedClient();
        var widgetId = await SeedPartAsync($"WIDGET-{Guid.NewGuid():N}".Substring(0, 14), "Widget Doodad");
        var bracketId = await SeedPartAsync($"BRACKET-{Guid.NewGuid():N}".Substring(0, 14), "Bracket");
        var listId = await SeedPriceListAsync($"PL-Search-{Guid.NewGuid():N}");

        await client.PostAsJsonAsync($"/api/v1/price-lists/{listId}/entries", BuildBody(widgetId));
        await client.PostAsJsonAsync($"/api/v1/price-lists/{listId}/entries", BuildBody(bracketId));

        var allResp = await client.GetFromJsonAsync<PagedResponse<PriceListEntryResponseModel>>(
            $"/api/v1/price-lists/{listId}/entries");
        allResp!.TotalCount.Should().Be(2);

        var searchResp = await client.GetFromJsonAsync<PagedResponse<PriceListEntryResponseModel>>(
            $"/api/v1/price-lists/{listId}/entries?search=WIDGET");
        searchResp!.TotalCount.Should().Be(1);
        searchResp.Items.Should().ContainSingle(e => e.PartId == widgetId);
    }

    [Fact]
    public async Task List_Pagination_RespectsPageSize()
    {
        await EnablePriceListCapabilityAsync();
        var client = AuthenticatedClient();
        var listId = await SeedPriceListAsync($"PL-Page-{Guid.NewGuid():N}");

        // Create 4 entries on different parts (uniqueness is on (list, part, minQty)).
        for (var i = 0; i < 4; i++)
        {
            var partId = await SeedPartAsync($"P-PG-{Guid.NewGuid():N}".Substring(0, 14), $"Page Part {i}");
            await client.PostAsJsonAsync(
                $"/api/v1/price-lists/{listId}/entries",
                BuildBody(partId, unitPrice: 1m + i, minQty: 1));
        }

        var pageOne = await client.GetFromJsonAsync<PagedResponse<PriceListEntryResponseModel>>(
            $"/api/v1/price-lists/{listId}/entries?page=1&pageSize=2");
        pageOne!.Items.Count.Should().Be(2);
        pageOne.TotalCount.Should().Be(4);
        pageOne.Page.Should().Be(1);
        pageOne.PageSize.Should().Be(2);

        var pageTwo = await client.GetFromJsonAsync<PagedResponse<PriceListEntryResponseModel>>(
            $"/api/v1/price-lists/{listId}/entries?page=2&pageSize=2");
        pageTwo!.Items.Count.Should().Be(2);
        pageTwo.TotalCount.Should().Be(4);
        pageTwo.Page.Should().Be(2);

        // No overlap between pages.
        var pageOneIds = pageOne.Items.Select(e => e.Id).ToHashSet();
        var pageTwoIds = pageTwo.Items.Select(e => e.Id).ToHashSet();
        pageOneIds.Overlaps(pageTwoIds).Should().BeFalse();
    }
}
