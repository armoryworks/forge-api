using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Capabilities;

namespace Forge.Tests.Remediation.SalesOrders;

/// <summary>
/// Region 2 · Sales Orders RED test (see ../README.md). Finding BE-1 / SO-8: SO lines were
/// immutable. Now GREEN — PUT /orders/{id}/lines/{lineId} edits a line, gated to Draft.
/// CAP-O2C-SO on. (The header PUT /orders/{id} already existed; SO-4 Draft-list visibility
/// remains tracked in the catalog.)
/// </summary>
[Collection(CapabilityTestCollection.Name)]
public class SalesOrdersRemediationTests
{
    private readonly CapabilityTestWebApplicationFactory _factory;
    public SalesOrdersRemediationTests(CapabilityTestWebApplicationFactory factory) => _factory = factory;

    private HttpClient AuthClient(string role = "Admin")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "1");
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    private IServiceScope NewScope() => _factory.Services.CreateScope();

    [Fact] // BE-1 / SO-8 GREEN — a draft sales-order line is editable
    public async Task Draft_sales_order_line_can_be_edited()
    {
        int orderId;
        int lineId;
        using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var customer = new Customer { Name = "SO8 Customer" };
            db.Customers.Add(customer);
            await db.SaveChangesAsync();

            var order = new SalesOrder
            {
                CustomerId = customer.Id,
                Status = SalesOrderStatus.Draft,
                Lines = { new SalesOrderLine { Description = "Original", Quantity = 1m, UnitPrice = 10m } },
            };
            db.SalesOrders.Add(order);
            await db.SaveChangesAsync();
            orderId = order.Id;
            lineId = order.Lines.First().Id;
        }

        var body = System.Net.Http.Json.JsonContent.Create(
            new { description = "Edited line", quantity = 4m, unitPrice = 25m, notes = "rev" });
        var response = await AuthClient().PutAsync($"/api/v1/orders/{orderId}/lines/{lineId}", body);

        response.IsSuccessStatusCode.Should().BeTrue("draft sales-order lines must be editable");
        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("Edited line", "the line edit must persist");
    }
}
