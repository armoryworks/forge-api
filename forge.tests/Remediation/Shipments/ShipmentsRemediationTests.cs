using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Capabilities;

namespace Forge.Tests.Remediation.Shipments;

/// <summary>
/// Region 2 · Shipments RED test (see ../README.md). Finding P06-3 / INV-1 / S-MV1:
/// ShipShipment is a status-flip only — it never relieves on-hand (InventoryReliefService
/// is orphaned) and never releases the SO-line reservation. This asserts shipping
/// decrements the bin's on-hand. CAP-O2C-SHIP is default-on.
/// (The reservation-release half of S-MV1 is owed too — tracked in the catalog.)
/// </summary>
[Collection(CapabilityTestCollection.Name)]
public class ShipmentsRemediationTests
{
    private readonly CapabilityTestWebApplicationFactory _factory;
    public ShipmentsRemediationTests(CapabilityTestWebApplicationFactory factory) => _factory = factory;

    private HttpClient AuthClient(string role = "Admin")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "1");
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    private IServiceScope NewScope() => _factory.Services.CreateScope();

    [Fact] // GREEN (P06-3 / S-MV1): shipping now relieves on-hand via InventoryReliefService.
    public async Task Shipping_relieves_on_hand_inventory()
    {
        int binId;
        int shipmentId;
        using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var part = new Part { PartNumber = $"P-SHIP-{Guid.NewGuid().ToString("N")[..8]}", Name = "Shipped Part" };
            db.Parts.Add(part);
            var customer = new Customer { Name = "P06-3 Ship Customer" };
            db.Customers.Add(customer);
            await db.SaveChangesAsync();

            var bin = new BinContent
            {
                LocationId = 1,
                EntityType = "part",
                EntityId = part.Id,
                Quantity = 10m,
                ReservedQuantity = 0m,
                Status = BinContentStatus.Stored,
                PlacedBy = 1,
                PlacedAt = DateTimeOffset.UtcNow,
            };
            db.Add(bin);

            var so = new SalesOrder { CustomerId = customer.Id, Status = SalesOrderStatus.Confirmed };
            db.SalesOrders.Add(so);
            await db.SaveChangesAsync();

            var shipment = new Shipment
            {
                SalesOrderId = so.Id,
                Status = ShipmentStatus.Packed,
                Lines = { new ShipmentLine { PartId = part.Id, Quantity = 3m } },
            };
            db.Shipments.Add(shipment);
            await db.SaveChangesAsync();

            binId = bin.Id;
            shipmentId = shipment.Id;
        }

        var shipResp = await AuthClient().PostAsync($"/api/v1/shipments/{shipmentId}/ship", null);
        shipResp.IsSuccessStatusCode.Should().BeTrue(await shipResp.Content.ReadAsStringAsync());

        using var verify = NewScope();
        var db2 = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        db2.BinContents.First(b => b.Id == binId).Quantity.Should().BeLessThan(10m,
            "shipping 3 units must relieve on-hand from 10 (today the bin is untouched)");
    }
}
