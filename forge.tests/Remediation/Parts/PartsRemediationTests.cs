using System.Net.Http.Json;

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

using Forge.Core.Entities;
using Forge.Data.Context;
using Forge.Tests.Capabilities;

namespace Forge.Tests.Remediation.Parts;

/// <summary>
/// Region 1 · Parts/BOM RED tests (see ../README.md).
/// Findings: D5 (BOM allows multi-node cycles — only direct self-ref is guarded),
/// D2b (inventory-summary returns total only, no reserved/available split).
/// CAP-MD-PARTS is default-on. UI rows tracked separately: P7, P6, D3, D8, D4.
/// </summary>
[Collection(CapabilityTestCollection.Name)]
public class PartsRemediationTests
{
    private readonly CapabilityTestWebApplicationFactory _factory;
    public PartsRemediationTests(CapabilityTestWebApplicationFactory factory) => _factory = factory;

    private HttpClient AuthClient(string role = "Admin")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "1");
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    private IServiceScope NewScope() => _factory.Services.CreateScope();

    private async Task<int> SeedPart(string suffix)
    {
        using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var part = new Part { PartNumber = $"P-{suffix}-{Guid.NewGuid().ToString("N")[..8]}", Name = $"Part {suffix}" };
        db.Parts.Add(part);
        await db.SaveChangesAsync();
        return part.Id;
    }

    [Fact] // D5 GREEN — BOM cycle (A→B→A) now rejected via descendant-walk in CreateBOMEntry
    public async Task Adding_a_BOM_edge_that_forms_a_cycle_is_rejected()
    {
        var a = await SeedPart("A");
        var b = await SeedPart("B");
        var client = AuthClient();

        // A → B  (legal)
        var first = await client.PostAsync($"/api/v1/parts/{a}/bom",
            JsonContent.Create(new { childPartId = b, quantity = 1m, sourceType = "Make" }));
        first.IsSuccessStatusCode.Should().BeTrue("A→B is a legal first edge");

        // B → A  (forms the cycle A→B→A)
        var cycle = await client.PostAsync($"/api/v1/parts/{b}/bom",
            JsonContent.Create(new { childPartId = a, quantity = 1m, sourceType = "Make" }));

        cycle.IsSuccessStatusCode.Should().BeFalse(
            "an edge that closes a BOM cycle must be rejected (recursive explosion would loop forever)");
    }

    [Fact(Skip = "RED: D2b — inventory-summary returns total quantity only, no reserved/available split. " +
                 "Remove Skip when the response includes reserved + available.")]
    public async Task Part_inventory_summary_includes_reserved_and_available()
    {
        var partId = await SeedPart("Sum");

        var json = await (await AuthClient().GetAsync($"/api/v1/parts/{partId}/inventory-summary"))
            .Content.ReadAsStringAsync();

        json.Should().Contain("reserved");
        json.Should().Contain("available",
            "callers need the reserved/available split, not just the on-hand total");
    }
}
