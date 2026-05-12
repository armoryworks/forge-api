using System.Net;
using System.Net.Http.Json;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Forge.Api.Capabilities;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Capabilities;

namespace Forge.Tests.Workflows;

/// <summary>
/// Integration coverage for the PriceList parent CRUD surface
/// (create / read / update / delete) added for the Customer Pricing tab UI.
///
/// PriceListEntry coverage already lives in
/// <see cref="PriceListEntryCrudTests"/>; this file focuses on the parent
/// row including <c>IsDefault</c> uniqueness logic (mirrors the
/// <c>VendorPart.IsPreferred</c> behaviour shipped in Pillar 3).
/// </summary>
[Collection(CapabilityTestCollection.Name)]
public class PriceListCrudTests(CapabilityTestWebApplicationFactory factory)
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

    private static CreatePriceListRequestModel BuildBody(
        string? name = null,
        int? customerId = null,
        bool isDefault = false,
        bool isActive = true) =>
        new(
            Name: name ?? $"PL-{Guid.NewGuid():N}".Substring(0, 12),
            Description: null,
            CustomerId: customerId,
            IsDefault: isDefault,
            EffectiveFrom: null,
            EffectiveTo: null,
            Entries: null,
            IsActive: isActive);

    [Fact]
    public async Task Create_EmptyList_Returns201_AndReadsBack()
    {
        await EnablePriceListCapabilityAsync();
        var client = AuthenticatedClient();

        var body = BuildBody(name: $"PL-Create-{Guid.NewGuid():N}".Substring(0, 16));
        var resp = await client.PostAsJsonAsync("/api/v1/price-lists", body);

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await resp.Content.ReadFromJsonAsync<PriceListListItemModel>();
        created.Should().NotBeNull();
        created!.Id.Should().BePositive();
        created.Name.Should().Be(body.Name);
        created.IsActive.Should().BeTrue();
        created.EntryCount.Should().Be(0);

        var read = await client.GetFromJsonAsync<PriceListResponseModel>(
            $"/api/v1/price-lists/{created.Id}");
        read!.Id.Should().Be(created.Id);
        read.Name.Should().Be(body.Name);
        read.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task Update_ChangesNameAndDescription()
    {
        await EnablePriceListCapabilityAsync();
        var client = AuthenticatedClient();

        var createResp = await client.PostAsJsonAsync(
            "/api/v1/price-lists",
            BuildBody(name: $"PL-Pre-{Guid.NewGuid():N}".Substring(0, 14)));
        var created = (await createResp.Content.ReadFromJsonAsync<PriceListListItemModel>())!;

        var newName = $"PL-Renamed-{Guid.NewGuid():N}".Substring(0, 18);
        var update = new UpdatePriceListRequestModel(
            Name: newName,
            Description: "Edited",
            IsDefault: false,
            IsActive: true,
            EffectiveFrom: null,
            EffectiveTo: null);

        var updateResp = await client.PutAsJsonAsync(
            $"/api/v1/price-lists/{created.Id}", update);
        updateResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var refreshed = await client.GetFromJsonAsync<PriceListResponseModel>(
            $"/api/v1/price-lists/{created.Id}");
        refreshed!.Name.Should().Be(newName);
        refreshed.Description.Should().Be("Edited");
    }

    [Fact]
    public async Task Delete_RemovesListFromGetAll()
    {
        await EnablePriceListCapabilityAsync();
        var client = AuthenticatedClient();

        var createResp = await client.PostAsJsonAsync(
            "/api/v1/price-lists",
            BuildBody(name: $"PL-Delete-{Guid.NewGuid():N}".Substring(0, 18)));
        var created = (await createResp.Content.ReadFromJsonAsync<PriceListListItemModel>())!;

        var deleteResp = await client.DeleteAsync($"/api/v1/price-lists/{created.Id}");
        deleteResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var listResp = await client.GetFromJsonAsync<List<PriceListListItemModel>>(
            "/api/v1/price-lists");
        listResp!.Should().NotContain(pl => pl.Id == created.Id,
            "soft-delete must hide the row from GetAll via the global query filter");
    }

    [Fact]
    public async Task SystemWide_And_CustomerScoped_Both_Supported()
    {
        await EnablePriceListCapabilityAsync();
        var client = AuthenticatedClient();
        var customerId = await SeedCustomerAsync();

        var systemResp = await client.PostAsJsonAsync(
            "/api/v1/price-lists",
            BuildBody(name: $"PL-Sys-{Guid.NewGuid():N}".Substring(0, 14)));
        systemResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var systemList = (await systemResp.Content.ReadFromJsonAsync<PriceListListItemModel>())!;
        systemList.CustomerId.Should().BeNull();

        var custResp = await client.PostAsJsonAsync(
            "/api/v1/price-lists",
            BuildBody(name: $"PL-Cust-{Guid.NewGuid():N}".Substring(0, 14), customerId: customerId));
        custResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var custList = (await custResp.Content.ReadFromJsonAsync<PriceListListItemModel>())!;
        custList.CustomerId.Should().Be(customerId);

        var customerListsResp = await client.GetFromJsonAsync<List<PriceListListItemModel>>(
            $"/api/v1/customers/{customerId}/price-lists");
        customerListsResp!.Should().Contain(pl => pl.Id == custList.Id);
        customerListsResp.Should().NotContain(pl => pl.Id == systemList.Id,
            "the customer-scoped read must not leak system-wide lists");
    }

    [Fact]
    public async Task IsDefault_Uniqueness_PerCustomerScope()
    {
        await EnablePriceListCapabilityAsync();
        var client = AuthenticatedClient();
        var customerId = await SeedCustomerAsync();

        // Create two lists for the same customer; first one is default.
        var firstResp = await client.PostAsJsonAsync(
            "/api/v1/price-lists",
            BuildBody(name: $"PL-Def1-{Guid.NewGuid():N}".Substring(0, 16),
                customerId: customerId, isDefault: true));
        var first = (await firstResp.Content.ReadFromJsonAsync<PriceListListItemModel>())!;

        var secondResp = await client.PostAsJsonAsync(
            "/api/v1/price-lists",
            BuildBody(name: $"PL-Def2-{Guid.NewGuid():N}".Substring(0, 16),
                customerId: customerId, isDefault: false));
        var second = (await secondResp.Content.ReadFromJsonAsync<PriceListListItemModel>())!;

        // Promote the second list to default — first should auto-clear.
        var update = new UpdatePriceListRequestModel(
            Name: second.Name, Description: null,
            IsDefault: true, IsActive: true,
            EffectiveFrom: null, EffectiveTo: null);
        var updateResp = await client.PutAsJsonAsync(
            $"/api/v1/price-lists/{second.Id}", update);
        updateResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var customerLists = await client.GetFromJsonAsync<List<PriceListListItemModel>>(
            $"/api/v1/customers/{customerId}/price-lists");
        customerLists.Should().NotBeNull();
        customerLists!.Single(pl => pl.Id == second.Id).IsDefault.Should().BeTrue();
        customerLists.Single(pl => pl.Id == first.Id).IsDefault.Should().BeFalse(
            "promoting another list to default must clear the previous default in the same scope");
    }

    private async Task<int> SeedCustomerAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var customer = new Core.Entities.Customer
        {
            Name = $"PL-Test-Cust-{Guid.NewGuid():N}".Substring(0, 24),
            IsActive = true,
        };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        return customer.Id;
    }
}
