using System.Net;
using System.Net.Http.Json;

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

using Forge.Data.Context;
using Forge.Tests.Capabilities;

namespace Forge.Tests.Remediation.Lots;

/// <summary>
/// Region 1 · Lots RED tests (see ../README.md). Finding L2: lots were create-only
/// (LotsController had GET/POST but no PUT/DELETE) so a mistaken lot couldn't be
/// corrected or archived despite the DeletedAt column. Now GREEN — update + soft-delete
/// endpoints exist. Lots sit behind CAP-INV-LOTS (default OFF), so each test enables it first.
/// </summary>
[Collection(CapabilityTestCollection.Name)]
public class LotsRemediationTests
{
    private readonly CapabilityTestWebApplicationFactory _factory;
    public LotsRemediationTests(CapabilityTestWebApplicationFactory factory) => _factory = factory;

    private HttpClient AuthClient(string role = "Admin")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "1");
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    private IServiceScope NewScope() => _factory.Services.CreateScope();

    private async Task EnableLots(HttpClient admin) =>
        await admin.PutAsync("/api/v1/capabilities/CAP-INV-LOTS/enabled", JsonContent.Create(new { enabled = true }));

    [Fact] // L2 GREEN — a lot's expiry/supplier-lot/notes are now correctable
    public async Task Lot_can_be_corrected()
    {
        var admin = AuthClient();
        await EnableLots(admin);

        int lotId;
        using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var part = new Part { PartNumber = $"P-LOT-{Guid.NewGuid().ToString("N")[..8]}", Name = "Lot Part" };
            db.Parts.Add(part);
            await db.SaveChangesAsync();

            var lot = new LotRecord { LotNumber = $"LOT-{Guid.NewGuid().ToString("N")[..8]}", PartId = part.Id, Quantity = 5m };
            db.LotRecords.Add(lot);
            await db.SaveChangesAsync();
            lotId = lot.Id;
        }

        var body = JsonContent.Create(new { supplierLotNumber = "SUP-9", notes = "Corrected note" });
        var response = await admin.PutAsync($"/api/v1/lots/{lotId}", body);

        response.IsSuccessStatusCode.Should().BeTrue("a mistaken lot must be correctable");
        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("Corrected note", "the correction must persist");
    }

    [Fact] // L2 GREEN — a mistaken lot can be soft-deleted
    public async Task Lot_can_be_soft_deleted()
    {
        var admin = AuthClient();
        await EnableLots(admin);

        int lotId;
        using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var lot = new LotRecord { LotNumber = $"LOT-{Guid.NewGuid().ToString("N")[..8]}", PartId = 1, Quantity = 5m };
            db.LotRecords.Add(lot);
            await db.SaveChangesAsync();
            lotId = lot.Id;
        }

        var response = await admin.DeleteAsync($"/api/v1/lots/{lotId}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, "a mistaken lot must be archivable (soft delete)");
    }
}
