using System.Net;
using System.Net.Http.Json;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Capabilities;

namespace Forge.Tests.Remediation.Customers;

/// <summary>
/// Region 1 · Customers RED tests (see ../README.md).
/// GREEN: C8 (deactivate-with-open-docs guard), C2 (bulk-import preview + commit).
/// Still RED: C3 (segments — needs a new entity + migration, a reviewed-session item).
/// CAP-MD-CUSTOMERS is default-on.
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

    [Fact] // C8 GREEN — deactivating a customer with an open invoice is rejected
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
            "a customer with open documents must not be deactivated");
    }

    [Fact] // C2 GREEN — bulk-intake preview classifies rows without persisting
    public async Task Customer_bulk_intake_preview_classifies_rows()
    {
        var name = $"Acme {Guid.NewGuid():N}";
        var body = JsonContent.Create(new
        {
            rows = new[]
            {
                new { externalRowKey = "r1", name, email = (string?)"acme@example.com", companyName = (string?)null, phone = (string?)null, notes = (string?)null },
                new { externalRowKey = "r2", name, email = (string?)null, companyName = (string?)null, phone = (string?)null, notes = (string?)null },
                new { externalRowKey = "r3", name = "", email = (string?)null, companyName = (string?)null, phone = (string?)null, notes = (string?)null },
            },
        });
        var response = await AuthClient().PostAsync("/api/v1/customers/bulk-intake/preview", body);

        response.IsSuccessStatusCode.Should().BeTrue();
        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("DuplicateWithinBatch", "the second row repeats the first row's name");
        json.Should().Contain("Invalid", "the blank-name row is invalid");
    }

    [Fact] // C2 GREEN — bulk-intake commit persists the clean rows
    public async Task Customer_bulk_intake_commit_creates_customers()
    {
        var name = $"CommitCo {Guid.NewGuid():N}";
        var body = JsonContent.Create(new
        {
            rows = new[]
            {
                new { externalRowKey = "c1", name, email = (string?)null, companyName = (string?)null, phone = (string?)null, notes = (string?)null },
            },
        });
        var response = await AuthClient().PostAsync("/api/v1/customers/bulk-intake/commit", body);
        response.IsSuccessStatusCode.Should().BeTrue();

        using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Customers.AnyAsync(c => c.Name == name)).Should().BeTrue("commit must persist the new customer");
    }

    [Fact] // C3 GREEN — customer segments are backed by real CRUD (replaces hard-coded examples)
    public async Task Customer_segment_can_be_created_and_listed()
    {
        var name = $"Aerospace Tier {Guid.NewGuid():N}";

        var create = await AuthClient().PostAsync("/api/v1/customers/segments",
            JsonContent.Create(new { name, description = "AS9100 accounts", filterCriteria = (string?)null }));
        create.IsSuccessStatusCode.Should().BeTrue("admins must be able to create a segment");
        (await create.Content.ReadAsStringAsync()).Should().Contain(name, "the created segment must round-trip");

        var list = await AuthClient().GetAsync("/api/v1/customers/segments");
        list.IsSuccessStatusCode.Should().BeTrue();
        (await list.Content.ReadAsStringAsync()).Should().Contain(name, "the new segment must appear in the list");
    }
}
