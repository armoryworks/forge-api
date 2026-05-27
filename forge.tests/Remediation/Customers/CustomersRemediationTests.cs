using System.Net;
using System.Net.Http.Json;

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Capabilities;

namespace Forge.Tests.Remediation.Customers;

/// <summary>
/// Region 1 · Customers RED tests (see ../README.md for the RED-pending convention).
/// Findings: C8 (deactivate with open docs, no guard), C2 (bulk-import is placeholder
/// chrome — no endpoint), C3 (segments page hardcoded — no endpoint).
/// UI-only rows tracked separately: C7 (tab caps), C5 (standalone /customers/contacts gate).
/// CAP-MD-CUSTOMERS is default-on, so these reach the handler/route.
/// </summary>
[Collection(CapabilityTestCollection.Name)]
public class CustomersRemediationTests
{
    private readonly CapabilityTestWebApplicationFactory _factory;
    public CustomersRemediationTests(CapabilityTestWebApplicationFactory factory) => _factory = factory;

    private HttpClient AuthClient(string role = "Admin")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "1");
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    private IServiceScope NewScope() => _factory.Services.CreateScope();

    [Fact(Skip = "RED: C8 — UpdateCustomer deactivates a customer with open invoices/orders/jobs " +
                 "with no guard. Remove Skip when deactivating a customer that has open documents " +
                 "is rejected (409).")]
    public async Task Deactivating_a_customer_with_an_open_invoice_is_rejected()
    {
        int customerId;
        using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var customer = new Customer { Name = "C8-OpenDocs" };
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
            customerId = customer.Id;

            db.Invoices.Add(new Invoice { CustomerId = customerId, Status = InvoiceStatus.Sent });
            await db.SaveChangesAsync();
        }

        var body = JsonContent.Create(new { isActive = false });
        var response = await AuthClient().PutAsync($"/api/v1/customers/{customerId}", body);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "a customer with open documents must not be deactivated (definition-of-correct)");
    }

    [Fact(Skip = "RED: C2 — customer bulk-import is placeholder UI with no backend endpoint. " +
                 "Remove Skip when POST /customers/bulk-intake/preview exists (mirrors leads bulk-intake).")]
    public async Task Customer_bulk_intake_preview_endpoint_exists()
    {
        var body = JsonContent.Create(new { rows = Array.Empty<object>() });
        var response = await AuthClient().PostAsync("/api/v1/customers/bulk-intake/preview", body);

        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
        response.StatusCode.Should().NotBe(HttpStatusCode.MethodNotAllowed,
            "the bulk-import wizard needs a real preview endpoint, not placeholder chrome");
    }

    [Fact(Skip = "RED: C3 — customer segments page hardcodes examples with no backend. " +
                 "Remove Skip when GET /customers/segments exists.")]
    public async Task Customer_segments_endpoint_exists()
    {
        var response = await AuthClient().GetAsync("/api/v1/customers/segments");

        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound,
            "segments must be backed by a real CRUD endpoint, not 4 hardcoded examples");
    }
}
