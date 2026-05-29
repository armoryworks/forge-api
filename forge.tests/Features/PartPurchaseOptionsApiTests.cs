using System.Net;
using System.Net.Http.Json;

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Capabilities;

namespace Forge.Tests.Features;

/// <summary>
/// UoM purchase-options effort — CRUD for a part's purchase options
/// (/api/v1/parts/{partId}/purchase-options). CAP-MD-PARTS is default-on.
/// </summary>
[Collection(CapabilityTestCollection.Name)]
public class PartPurchaseOptionsApiTests
{
    private readonly CapabilityTestWebApplicationFactory _factory;
    public PartPurchaseOptionsApiTests(CapabilityTestWebApplicationFactory factory) => _factory = factory;

    private HttpClient AuthClient(string role = "Admin")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "1");
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    private IServiceScope NewScope() => _factory.Services.CreateScope();

    private async Task<int> SeedPart(int? stockUomId = null)
    {
        using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var part = new Part { PartNumber = $"P-{Guid.NewGuid().ToString("N")[..8]}", Name = "Sheet stock", StockUomId = stockUomId };
        db.Parts.Add(part);
        await db.SaveChangesAsync();
        return part.Id;
    }

    [Fact]
    public async Task Purchase_option_round_trips_through_create_list_update_delete()
    {
        var partId = await SeedPart();
        var client = AuthClient();

        // Create
        var create = await client.PostAsync($"/api/v1/parts/{partId}/purchase-options",
            JsonContent.Create(new { label = "4x8 sheet", contentQuantity = 32m, contentUomId = (int?)null, sortOrder = 0 }));
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdJson = await create.Content.ReadAsStringAsync();
        createdJson.Should().Contain("4x8 sheet");

        // List
        var listJson = await (await client.GetAsync($"/api/v1/parts/{partId}/purchase-options")).Content.ReadAsStringAsync();
        listJson.Should().Contain("4x8 sheet").And.Contain("32");

        var id = (await create.Content.ReadFromJsonAsync<OptionDto>())!.Id;

        // Update
        var update = await client.PutAsync($"/api/v1/parts/{partId}/purchase-options/{id}",
            JsonContent.Create(new { label = "4x8 sheet (rev B)", contentQuantity = 32m, contentUomId = (int?)null, sortOrder = 1, isActive = true }));
        update.IsSuccessStatusCode.Should().BeTrue();
        (await update.Content.ReadAsStringAsync()).Should().Contain("rev B");

        // Delete (soft) → drops out of the list
        var del = await client.DeleteAsync($"/api/v1/parts/{partId}/purchase-options/{id}");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var afterJson = await (await client.GetAsync($"/api/v1/parts/{partId}/purchase-options")).Content.ReadAsStringAsync();
        afterJson.Should().NotContain("rev B");
    }

    [Fact]
    public async Task Create_rejects_a_content_uom_in_a_different_category_than_the_part_stock_uom()
    {
        int areaUomId, massUomId;
        using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var area = new UnitOfMeasure { Code = $"SQFT{Guid.NewGuid().ToString("N")[..4]}", Name = "Square Foot", Category = UomCategory.Area };
            var mass = new UnitOfMeasure { Code = $"G{Guid.NewGuid().ToString("N")[..4]}", Name = "Gram", Category = UomCategory.Weight };
            db.UnitsOfMeasure.AddRange(area, mass);
            await db.SaveChangesAsync();
            areaUomId = area.Id;
            massUomId = mass.Id;
        }
        var partId = await SeedPart(stockUomId: areaUomId);

        // Area part, mass content UoM → mismatch → 409.
        var resp = await AuthClient().PostAsync($"/api/v1/parts/{partId}/purchase-options",
            JsonContent.Create(new { label = "bad option", contentQuantity = 8m, contentUomId = massUomId, sortOrder = 0 }));

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "a mass content UoM cannot describe an area part's purchase option");
    }

    private sealed record OptionDto(int Id);
}
