using System.Net.Http.Json;

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Capabilities;

namespace Forge.Tests.Remediation.Invoices;

/// <summary>
/// Region 2 · Invoices RED test (see ../README.md). Finding AUDIT-21-S1 / P06-9 (BLOCKER):
/// creating an invoice never enqueues a QBO SyncQueueEntry — the AR→accounting pipe is
/// severed (only MoveJobStage enqueues). This asserts an invoice create produces a
/// sync-queue row. CAP-O2C-INVOICE is default-on.
/// (P06-1 invoiced≤shipped and P06-6 invoice line-edit are tracked in the catalog;
/// they need shipment context / a from-job $0 invoice and are deferred.)
/// </summary>
[Collection(CapabilityTestCollection.Name)]
public class InvoicesRemediationTests
{
    private readonly CapabilityTestWebApplicationFactory _factory;
    public InvoicesRemediationTests(CapabilityTestWebApplicationFactory factory) => _factory = factory;

    private HttpClient AuthClient(string role = "Admin")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "1");
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    private IServiceScope NewScope() => _factory.Services.CreateScope();

    [Fact] // AUDIT-21-S1 / P06-9 (was RED): CreateInvoice now enqueues a QBO SyncQueueEntry.
    public async Task Creating_an_invoice_enqueues_a_sync_row()
    {
        int customerId;
        using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var customer = new Customer { Name = "AUDIT21-Invoice" };
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
            customerId = customer.Id;
        }

        var body = JsonContent.Create(new
        {
            customerId,
            invoiceDate = DateTimeOffset.UtcNow,
            dueDate = DateTimeOffset.UtcNow.AddDays(30),
            taxRate = 0m,
            lines = new[] { new { description = "Widget", quantity = 1m, unitPrice = 10m } },
        });
        await AuthClient().PostAsync("/api/v1/invoices", body);

        using var verify = NewScope();
        var db2 = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        db2.SyncQueueEntries.Any().Should().BeTrue(
            "an AR invoice must enqueue a QBO sync row so accounting stays in sync (today nothing enqueues)");
    }

    [Fact] // AUDIT-P06-1 / Q2C-BE-8 — invoicing more than has shipped is rejected.
    public async Task Invoicing_more_than_shipped_is_rejected()
    {
        int customerId, salesOrderId, partId;
        using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var customer = new Customer { Name = "P06-1 Customer" };
            var part = new Part { PartNumber = $"P-INV-{Guid.NewGuid().ToString("N")[..8]}", Name = "Widget" };
            db.Customers.Add(customer);
            db.Parts.Add(part);
            await db.SaveChangesAsync();

            var so = new SalesOrder { CustomerId = customer.Id, Status = SalesOrderStatus.Confirmed };
            db.SalesOrders.Add(so);
            await db.SaveChangesAsync();

            db.Shipments.Add(new Shipment
            {
                SalesOrderId = so.Id,
                Status = ShipmentStatus.Shipped,
                Lines = { new ShipmentLine { PartId = part.Id, Quantity = 5m } },
            });
            await db.SaveChangesAsync();

            customerId = customer.Id; salesOrderId = so.Id; partId = part.Id;
        }

        // 10 requested vs 5 shipped → rejected.
        var over = JsonContent.Create(new
        {
            customerId, salesOrderId,
            invoiceDate = DateTimeOffset.UtcNow, dueDate = DateTimeOffset.UtcNow.AddDays(30), taxRate = 0m,
            lines = new[] { new { partId, description = "Widget", quantity = 10m, unitPrice = 10m } },
        });
        (await AuthClient().PostAsync("/api/v1/invoices", over)).IsSuccessStatusCode
            .Should().BeFalse("invoicing 10 when only 5 shipped must be rejected (AUDIT-P06-1)");

        // 5 requested vs 5 shipped → allowed.
        var ok = JsonContent.Create(new
        {
            customerId, salesOrderId,
            invoiceDate = DateTimeOffset.UtcNow, dueDate = DateTimeOffset.UtcNow.AddDays(30), taxRate = 0m,
            lines = new[] { new { partId, description = "Widget", quantity = 5m, unitPrice = 10m } },
        });
        (await AuthClient().PostAsync("/api/v1/invoices", ok)).IsSuccessStatusCode
            .Should().BeTrue("invoicing exactly the shipped quantity must be allowed");
    }
}
