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

    [Fact(Skip = "RED: P06-4 — PO lines are immutable (no line edit endpoint). " +
                 "Remove Skip when PUT /api/v1/purchase-orders/{id}/lines/{lineId} exists.")]
    public async Task PurchaseOrder_line_edit_endpoint_exists()
    {
        var response = await AuthClient().PutAsync("/api/v1/purchase-orders/1/lines/1", null);

        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
        response.StatusCode.Should().NotBe(HttpStatusCode.MethodNotAllowed,
            "a draft PO's lines must be editable before submit");
    }

    [Fact(Skip = "RED: PRI-1/2/3 / P06-2 — receiving a PO line writes no BinContent, so on-hand " +
                 "never rises (the PO flips to Received and signals 'Materials Ready' on nothing). " +
                 "Remove Skip when receiving stocks the part (a BinContent row is created).")]
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

        var body = JsonContent.Create(new { lines = new[] { new { purchaseOrderLineId = lineId, receivedQuantity = 5m } } });
        await AuthClient().PostAsync($"/api/v1/purchase-orders/{poId}/receive", body);

        using var verify = NewScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        verifyDb.BinContents.Any(b => b.EntityType == "Part" && b.EntityId == partId).Should().BeTrue(
            "receiving must stock the part — otherwise on-hand stays at zero while the PO says Received");
    }
}
