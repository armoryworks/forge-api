using System.Net;
using System.Net.Http.Json;

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Capabilities;

namespace Forge.Tests.Remediation.PurchaseOrders;

/// <summary>
/// Region 2 · Purchase Orders + Receiving RED tests (see ../README.md).
/// Findings: P06-4 (PO lines immutable — no line edit endpoint), PRI-1/2/3 / P06-2
/// (PO-side receive marks the PO received + signals "Materials Ready" but writes NO
/// BinContent — stock never rises). CAP-P2P-PO is default-on.
/// </summary>
[Collection(CapabilityTestCollection.Name)]
public class PurchaseOrdersRemediationTests
{
    private readonly CapabilityTestWebApplicationFactory _factory;
    public PurchaseOrdersRemediationTests(CapabilityTestWebApplicationFactory factory) => _factory = factory;

    private HttpClient AuthClient(string role = "Admin")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "1");
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    private IServiceScope NewScope() => _factory.Services.CreateScope();

    [Fact] // P06-4 GREEN — a draft PO line is editable (gated to Draft)
    public async Task Draft_purchase_order_line_can_be_edited()
    {
        int poId;
        int lineId;
        using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var vendor = new Vendor { CompanyName = "P06-4 Vendor" };
            db.Vendors.Add(vendor);
            var part = new Part { PartNumber = $"P-POE-{Guid.NewGuid().ToString("N")[..8]}", Name = "PO Edit Part" };
            db.Parts.Add(part);
            await db.SaveChangesAsync();

            var po = new PurchaseOrder
            {
                VendorId = vendor.Id,
                Status = PurchaseOrderStatus.Draft,
                Lines = { new PurchaseOrderLine { PartId = part.Id, Description = "Original", OrderedQuantity = 2m, UnitPrice = 5m } },
            };
            db.PurchaseOrders.Add(po);
            await db.SaveChangesAsync();
            poId = po.Id;
            lineId = po.Lines.First().Id;
        }

        var body = JsonContent.Create(new { description = "Edited PO line", quantity = 7m, unitPrice = 9m, notes = "rev" });
        var response = await AuthClient().PutAsync($"/api/v1/purchase-orders/{poId}/lines/{lineId}", body);

        response.IsSuccessStatusCode.Should().BeTrue("draft PO lines must be editable before submit");
        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("Edited PO line", "the line edit must persist");
    }

    [Fact] // GREEN (PRI-1/2/3 / P06-2): receiving now stocks the part — a BinContent row is created/incremented.
    public async Task Receiving_a_PO_line_creates_stock()
    {
        int poId;
        int partId;
        using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var part = new Part { PartNumber = $"P-RCV-{Guid.NewGuid().ToString("N")[..8]}", Name = "Received Part" };
            db.Parts.Add(part);
            await db.SaveChangesAsync();
            partId = part.Id;

            var po = new PurchaseOrder
            {
                VendorId = 1,
                Status = PurchaseOrderStatus.Submitted,
                Lines = { new PurchaseOrderLine { PartId = partId, Description = "Received Part", OrderedQuantity = 5m, UnitPrice = 1m } },
            };
            db.PurchaseOrders.Add(po);
            await db.SaveChangesAsync();
            poId = po.Id;
        }

        int lineId;
        using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            lineId = db.PurchaseOrderLines.First(l => l.PurchaseOrderId == poId).Id;
        }

        var body = JsonContent.Create(new { lines = new[] { new { lineId, quantity = 5m } } });
        var receiveResponse = await AuthClient().PostAsync($"/api/v1/purchase-orders/{poId}/receive", body);
        receiveResponse.IsSuccessStatusCode.Should().BeTrue("the receive must succeed before it can stock the part");

        using var verify = NewScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        verifyDb.BinContents.Any(b => b.EntityType == "part" && b.EntityId == partId).Should().BeTrue(
            "receiving must stock the part — otherwise on-hand stays at zero while the PO says Received");
    }
}
