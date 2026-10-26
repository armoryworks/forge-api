using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Capabilities;

namespace Forge.Tests.Concurrency;

/// <summary>
/// The sales-order read hands the UI the order's version, in the body and as the ETag, so the header
/// save sends If-Match: of two saves made from the same read, the second is refused with 412 instead
/// of silently overwriting the first.
/// </summary>
[Collection(CapabilityTestCollection.Name)]
public class SalesOrderIfMatchTests(CapabilityTestWebApplicationFactory factory)
{
    private HttpClient AuthClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "1");
        client.DefaultRequestHeaders.Add("X-Test-Role", "Admin");
        return client;
    }

    private async Task<int> SeedDraftOrderAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var customer = new Customer { Name = "If-Match Customer" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        var order = new SalesOrder
        {
            CustomerId = customer.Id,
            Status = SalesOrderStatus.Draft,
            Lines = { new SalesOrderLine { Description = "Widget", Quantity = 1m, UnitPrice = 10m } },
        };
        db.SalesOrders.Add(order);
        await db.SaveChangesAsync();
        return order.Id;
    }

    private static HttpRequestMessage Put(int orderId, string etag, string customerPo) =>
        new(HttpMethod.Put, $"/api/v1/orders/{orderId}")
        {
            Headers = { { "If-Match", etag } },
            Content = JsonContent.Create(new { customerPO = customerPo, taxRate = 0m }),
        };

    [Fact]
    public async Task GetSalesOrder_ReturnsTheVersionAsRowVersionAndETag()
    {
        var orderId = await SeedDraftOrderAsync();

        var response = await AuthClient().GetAsync($"/api/v1/orders/{orderId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var rowVersion = body.RootElement.GetProperty("rowVersion").GetUInt32();
        rowVersion.Should().BeGreaterThan(0u);
        response.Headers.ETag!.Tag.Should().Be($"\"{rowVersion}\"");
    }

    [Fact]
    public async Task TwoSavesFromTheSameRead_TheSecondIsRefusedWith412()
    {
        var orderId = await SeedDraftOrderAsync();
        var client = AuthClient();
        var read = await client.GetAsync($"/api/v1/orders/{orderId}");
        var etag = read.Headers.ETag!.Tag;

        var first = await client.SendAsync(Put(orderId, etag, "PO-FIRST"));
        var second = await client.SendAsync(Put(orderId, etag, "PO-SECOND"));

        first.StatusCode.Should().Be(HttpStatusCode.NoContent, await first.Content.ReadAsStringAsync());
        second.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
        var after = await client.GetFromJsonAsync<JsonElement>($"/api/v1/orders/{orderId}");
        after.GetProperty("customerPO").GetString().Should().Be("PO-FIRST");
    }
}
