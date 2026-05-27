using System.Net;
using System.Net.Http.Json;

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Capabilities;

namespace Forge.Tests.Remediation.Inventory;

/// <summary>
/// Region 1 · Inventory RED tests (see ../README.md).
/// Findings: S-RI1 (Adjust/Transfer/CycleCount/RemoveBinContent ignore ReservedQuantity,
/// inflating available), S1 (stock list omits zero-stock parts), S2a (no PUT to update a
/// storage location). CAP-INV-CORE is default-on. UI rows tracked separately:
/// S2c/SO1/SO2 (bin pickers), cap-UX-INV. The reservation guard is shown here via Adjust;
/// the same guard is owed on Transfer/CycleCount-approve/RemoveBinContent.
/// </summary>
[Collection(CapabilityTestCollection.Name)]
public class InventoryRemediationTests
{
    private readonly CapabilityTestWebApplicationFactory _factory;
    public InventoryRemediationTests(CapabilityTestWebApplicationFactory factory) => _factory = factory;

    private HttpClient AuthClient(string role = "Admin")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "1");
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    private IServiceScope NewScope() => _factory.Services.CreateScope();

    [Fact] // S-RI1 GREEN (AdjustStock) — adjust below reserved now rejected; same guard owed on Transfer/CycleCount/Remove
    public async Task Adjusting_a_bin_below_its_reserved_quantity_is_rejected()
    {
        int binContentId;
        using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var location = new StorageLocation { Name = "S-RI1 Bin", LocationType = LocationType.Bin };
            db.StorageLocations.Add(location);
            await db.SaveChangesAsync();

            var bin = new BinContent
            {
                LocationId = location.Id,
                EntityType = "Part",
                EntityId = 1,
                Quantity = 10m,
                ReservedQuantity = 8m,
                Status = BinContentStatus.Stored,
                PlacedBy = 1,
                PlacedAt = DateTimeOffset.UtcNow,
            };
            db.Add(bin);
            await db.SaveChangesAsync();
            binContentId = bin.Id;
        }

        var body = JsonContent.Create(new { binContentId, newQuantity = 2m, reason = "Correction" });
        var response = await AuthClient().PostAsync("/api/v1/inventory/adjust", body);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "8 units are reserved — the bin cannot be adjusted down to 2 (would inflate available)");
    }

    [Fact(Skip = "RED: S1 — the stock list omits parts that have zero on-hand (joins only parts " +
                 "with BinContent rows). Remove Skip when a zero-stock part appears in /inventory/parts.")]
    public async Task Stock_list_includes_a_part_with_zero_on_hand()
    {
        string partNumber;
        using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var part = new Part { PartNumber = $"P-ZERO-{Guid.NewGuid().ToString("N")[..8]}", Name = "Zero Stock" };
            db.Parts.Add(part);
            await db.SaveChangesAsync();
            partNumber = part.PartNumber;
        }

        var json = await (await AuthClient().GetAsync("/api/v1/inventory/parts")).Content.ReadAsStringAsync();

        json.Should().Contain(partNumber,
            "a part with no stock should still show in the inventory list (as zero), not vanish");
    }

    [Fact(Skip = "RED: S2a — there is no PUT to update a storage location (can't rename/re-type/re-parent). " +
                 "Remove Skip when PUT /inventory/locations/{id} exists.")]
    public async Task Storage_location_update_endpoint_exists()
    {
        var body = JsonContent.Create(new { name = "Renamed" });
        var response = await AuthClient().PutAsync("/api/v1/inventory/locations/1", body);

        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
        response.StatusCode.Should().NotBe(HttpStatusCode.MethodNotAllowed,
            "created locations must be editable (rename / re-type / re-parent)");
    }
}
