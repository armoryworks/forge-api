using System.Net.Http.Json;

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

using Forge.Data.Context;
using Forge.Tests.Capabilities;

namespace Forge.Tests.Remediation.RecurringOrders;

/// <summary>
/// Region 2 · Recurring Orders RED test (see ../README.md). Finding BE-4: a recurring
/// order had no update endpoint (only GET/POST/DELETE) — editing meant delete+recreate.
/// Now GREEN — PUT /api/v1/recurring-orders/{id} edits the header in place and (optionally)
/// replaces the line set. CAP-O2C-RECURRING is default-OFF, so the test enables it first.
/// BE-5 (RecurringOrderJob uses non-sequential numbers + skips the cap check) is a
/// Hangfire-job behavior, tracked in the catalog.
/// </summary>
[Collection(CapabilityTestCollection.Name)]
public class RecurringOrdersRemediationTests
{
    private readonly CapabilityTestWebApplicationFactory _factory;
    public RecurringOrdersRemediationTests(CapabilityTestWebApplicationFactory factory) => _factory = factory;

    private HttpClient AuthClient(string role = "Admin")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "1");
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    private IServiceScope NewScope() => _factory.Services.CreateScope();

    private async Task EnableRecurring(HttpClient admin) =>
        await admin.PutAsync("/api/v1/capabilities/CAP-O2C-RECURRING/enabled", JsonContent.Create(new { enabled = true }));

    [Fact] // BE-4 GREEN — a recurring order is editable in place (header fields)
    public async Task Recurring_order_is_editable_in_place()
    {
        var admin = AuthClient();
        await EnableRecurring(admin);

        int roId;
        using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var customer = new Customer { Name = "BE4-Recurring Co" };
            db.Customers.Add(customer);
            var part = new Part { PartNumber = $"P-RO-{Guid.NewGuid().ToString("N")[..8]}", Name = "RO Part" };
            db.Parts.Add(part);
            await db.SaveChangesAsync();

            var ro = new RecurringOrder
            {
                Name = "Weekly Widget",
                CustomerId = customer.Id,
                IntervalDays = 7,
                NextGenerationDate = DateTimeOffset.UtcNow.AddDays(7),
                Lines = { new RecurringOrderLine { PartId = part.Id, Description = "Widget", Quantity = 2m, UnitPrice = 10m, LineNumber = 1 } },
            };
            db.RecurringOrders.Add(ro);
            await db.SaveChangesAsync();
            roId = ro.Id;
        }

        var body = JsonContent.Create(new { name = "Biweekly Widget", intervalDays = 14, isActive = false });
        var response = await admin.PutAsync($"/api/v1/recurring-orders/{roId}", body);

        response.IsSuccessStatusCode.Should().BeTrue("a recurring order must be editable in place, not delete-and-recreate");
        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("Biweekly Widget", "the renamed schedule must persist");
        json.Should().Contain("\"intervalDays\":14", "the new interval must persist");
    }
}
