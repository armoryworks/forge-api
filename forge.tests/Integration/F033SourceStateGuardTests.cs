using System.Net;
using System.Net.Http.Json;

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Capabilities;

namespace Forge.Tests.Integration;

/// <summary>
/// F-033 / INV-SF2 HTTP regression net — illegal source-state → 409 Conflict.
///
/// Auth: TestAuthHandler (X-Test-User / X-Test-Role headers) — the proven pattern
/// used by every passing authenticated integration test in this suite.
/// Harness: CapabilityTestWebApplicationFactory (InMemory DB, ephemeral).
/// Does NOT touch the shared :4200 demo seed and does NOT trigger a rebuild.
///
/// Run target: any build at or after 5f6bef1. SHA-agnostic — asserts behavior.
/// </summary>
[Collection(CapabilityTestCollection.Name)]
public class F033SourceStateGuardTests
{
    private readonly CapabilityTestWebApplicationFactory _factory;

    public F033SourceStateGuardTests(CapabilityTestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient AuthClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "1");
        client.DefaultRequestHeaders.Add("X-Test-Role", "Admin");
        return client;
    }

    private IServiceScope NewScope() => _factory.Services.CreateScope();

    // ── VoidInvoice ───────────────────────────────────────────────────────────
    // Whitelist: {Sent, PartiallyPaid, Overdue}. All other states → 409.

    [Fact]
    public async Task VoidInvoice_FromDraftStatus_Returns409()
    {
        int invoiceId;
        using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var customer = new Customer { Name = "F033-VoidDraft" };
            db.Customers.Add(customer);
            await db.SaveChangesAsync();

            var invoice = new Invoice { CustomerId = customer.Id, Status = InvoiceStatus.Draft };
            db.Invoices.Add(invoice);
            await db.SaveChangesAsync();
            invoiceId = invoice.Id;
        }

        var response = await AuthClient().PostAsync($"/api/v1/invoices/{invoiceId}/void", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "Draft is outside the {Sent, PartiallyPaid, Overdue} whitelist");
    }

    [Fact]
    public async Task VoidInvoice_AlreadyVoided_Returns409()
    {
        int invoiceId;
        using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var customer = new Customer { Name = "F033-ReVoid" };
            db.Customers.Add(customer);
            await db.SaveChangesAsync();

            var invoice = new Invoice { CustomerId = customer.Id, Status = InvoiceStatus.Voided };
            db.Invoices.Add(invoice);
            await db.SaveChangesAsync();
            invoiceId = invoice.Id;
        }

        var response = await AuthClient().PostAsync($"/api/v1/invoices/{invoiceId}/void", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "Re-void of a Voided invoice was the primary silent-duplicate exposure pre-F033");
    }

    // ── CancelSalesOrder ──────────────────────────────────────────────────────
    // Whitelist: {Draft, Confirmed, PartiallyShipped}. All other states → 409.

    [Fact]
    public async Task CancelSalesOrder_FromInProductionStatus_Returns409()
    {
        int soId;
        using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var customer = new Customer { Name = "F033-CancelInProd" };
            db.Customers.Add(customer);
            await db.SaveChangesAsync();

            var so = new SalesOrder { CustomerId = customer.Id, Status = SalesOrderStatus.InProduction };
            db.SalesOrders.Add(so);
            await db.SaveChangesAsync();
            soId = so.Id;
        }

        var response = await AuthClient().PostAsync($"/api/v1/orders/{soId}/cancel", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "InProduction is outside the {Draft, Confirmed, PartiallyShipped} whitelist");
    }

    [Fact]
    public async Task CancelSalesOrder_AlreadyCancelled_Returns409()
    {
        int soId;
        using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var customer = new Customer { Name = "F033-SOReCancel" };
            db.Customers.Add(customer);
            await db.SaveChangesAsync();

            var so = new SalesOrder { CustomerId = customer.Id, Status = SalesOrderStatus.Cancelled };
            db.SalesOrders.Add(so);
            await db.SaveChangesAsync();
            soId = so.Id;
        }

        var response = await AuthClient().PostAsync($"/api/v1/orders/{soId}/cancel", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "Re-cancel of a Cancelled SO was a silent no-op → 200 before F-033");
    }

    // ── CancelPurchaseOrder ───────────────────────────────────────────────────
    // Whitelist: {Draft, Submitted, Acknowledged}. All other states → 409.

    [Fact]
    public async Task CancelPurchaseOrder_FromPartiallyReceived_Returns409()
    {
        int poId;
        using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var vendor = new Vendor { CompanyName = "F033-CancelPartial" };
            db.Vendors.Add(vendor);
            await db.SaveChangesAsync();

            var po = new PurchaseOrder { VendorId = vendor.Id, Status = PurchaseOrderStatus.PartiallyReceived };
            db.PurchaseOrders.Add(po);
            await db.SaveChangesAsync();
            poId = po.Id;
        }

        var response = await AuthClient().PostAsync($"/api/v1/purchase-orders/{poId}/cancel", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "PartiallyReceived has committed stock on dock — cancel must be blocked");
    }

    [Fact]
    public async Task CancelPurchaseOrder_AlreadyCancelled_Returns409()
    {
        int poId;
        using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var vendor = new Vendor { CompanyName = "F033-POReCancel" };
            db.Vendors.Add(vendor);
            await db.SaveChangesAsync();

            var po = new PurchaseOrder { VendorId = vendor.Id, Status = PurchaseOrderStatus.Cancelled };
            db.PurchaseOrders.Add(po);
            await db.SaveChangesAsync();
            poId = po.Id;
        }

        var response = await AuthClient().PostAsync($"/api/v1/purchase-orders/{poId}/cancel", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "Re-cancel of a Cancelled PO was a silent duplicate → 200 before F-033");
    }

    // ── ReceiveItems ──────────────────────────────────────────────────────────
    // Whitelist: {Submitted, Acknowledged, PartiallyReceived}. All other states → 409.

    [Fact]
    public async Task ReceiveItems_OnDraftPO_Returns409()
    {
        int poId;
        using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var vendor = new Vendor { CompanyName = "F033-ReceiveDraft" };
            db.Vendors.Add(vendor);
            await db.SaveChangesAsync();

            var po = new PurchaseOrder { VendorId = vendor.Id, Status = PurchaseOrderStatus.Draft };
            db.PurchaseOrders.Add(po);
            await db.SaveChangesAsync();
            poId = po.Id;
        }

        var body = JsonContent.Create(new { Lines = new[] { new { LineId = 99, Quantity = 1 } } });
        var response = await AuthClient().PostAsync($"/api/v1/purchase-orders/{poId}/receive", body);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "Draft PO is outside {Submitted, Acknowledged, PartiallyReceived}");
    }

    // ── INV-SF2: CreateMaterialIssue on archived job ──────────────────────────

    [Fact]
    public async Task CreateMaterialIssue_OnArchivedJob_Returns409()
    {
        int jobId;
        int partId;
        using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var part = new Part
            {
                PartNumber = $"P-F033-{Guid.NewGuid().ToString("N")[..8]}",
                Name = "Guard Part",
            };
            db.Parts.Add(part);
            await db.SaveChangesAsync();
            partId = part.Id;

            var job = new Job
            {
                JobNumber = $"J-F033-{Guid.NewGuid().ToString("N")[..8]}",
                IsArchived = true,
            };
            db.Jobs.Add(job);
            await db.SaveChangesAsync();
            jobId = job.Id;
        }

        var body = JsonContent.Create(new
        {
            PartId = partId,
            Quantity = 1,
            IssueType = 0,   // MaterialIssueType.Issue
            IssuedById = 1,
        });
        var response = await AuthClient().PostAsync($"/api/v1/jobs/{jobId}/material-issues", body);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "INV-SF2: material may not be issued to an archived job");
    }
}
