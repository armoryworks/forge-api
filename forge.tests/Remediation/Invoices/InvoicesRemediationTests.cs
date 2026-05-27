using System.Net.Http.Json;

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

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

    [Fact(Skip = "RED: AUDIT-21-S1 / P06-9 — creating an invoice never enqueues a QBO sync row. " +
                 "Remove Skip when CreateInvoice enqueues a SyncQueueEntry (integrated mode).")]
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
}
