using System.Net;
using System.Net.Http.Json;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Forge.Api.Features.Tolerances;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Capabilities;

namespace Forge.Tests.Handlers.Tolerances;

[Collection(CapabilityTestCollection.Name)]
public class ToleranceRaiseEndpointTests(CapabilityTestWebApplicationFactory factory)
{
    private HttpClient Client(string role)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "1");
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    private async Task<T> InDbAsync<T>(Func<AppDbContext, Task<T>> work)
    {
        using var scope = factory.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private Task<T> AddAsync<T>(T entity) where T : class => InDbAsync(async db =>
    {
        db.Add(entity);
        await db.SaveChangesAsync();
        return entity;
    });

    private static async Task ShouldBeRefusedAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain(ToleranceRaiseGuard.Code);
    }

    [Fact]
    public async Task A_vendor_tolerance_is_raised_by_a_manager_and_not_by_procurement()
    {
        var vendor = await AddAsync(new Vendor { CompanyName = $"Raise {Guid.NewGuid():N}"[..20], OverReceiptTolerancePct = 5m });
        var raise = new { overReceiptTolerancePct = 12.5m };

        await ShouldBeRefusedAsync(await Client("Procurement").PutAsJsonAsync($"/api/v1/vendors/{vendor.Id}", raise));
        (await InDbAsync(db => db.Vendors.AsNoTracking().Where(v => v.Id == vendor.Id).Select(v => v.OverReceiptTolerancePct).SingleAsync()))
            .Should().Be(5m);

        (await Client("Procurement").PutAsJsonAsync($"/api/v1/vendors/{vendor.Id}", new { overReceiptTolerancePct = 2m }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Client("Manager").PutAsJsonAsync($"/api/v1/vendors/{vendor.Id}", raise))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await InDbAsync(db => db.Vendors.AsNoTracking().Where(v => v.Id == vendor.Id).Select(v => v.OverReceiptTolerancePct).SingleAsync()))
            .Should().Be(12.5m);
    }

    [Fact]
    public async Task A_customer_tolerance_is_raised_by_a_manager_and_not_by_the_office_manager()
    {
        var customer = await AddAsync(new Customer { Name = $"Raise {Guid.NewGuid():N}"[..20], OverShipTolerancePct = 3m });
        var raise = new { overShipTolerancePct = 10m };

        await ShouldBeRefusedAsync(await Client("OfficeManager").PutAsJsonAsync($"/api/v1/customers/{customer.Id}", raise));

        (await Client("Manager").PutAsJsonAsync($"/api/v1/customers/{customer.Id}", raise))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await InDbAsync(db => db.Customers.AsNoTracking().Where(c => c.Id == customer.Id).Select(c => c.OverShipTolerancePct).SingleAsync()))
            .Should().Be(10m);
    }

    [Fact]
    public async Task A_purchase_order_line_tolerance_is_raised_by_a_manager_and_not_by_procurement()
    {
        var vendor = await AddAsync(new Vendor { CompanyName = $"Raise {Guid.NewGuid():N}"[..20], OverReceiptTolerancePct = 5m });
        var po = await AddAsync(new PurchaseOrder
        {
            PONumber = $"PO-{Guid.NewGuid():N}"[..16], VendorId = vendor.Id, Status = PurchaseOrderStatus.Draft,
            Lines = [new PurchaseOrderLine { Description = "Resin", OrderedQuantity = 100m, UnitPrice = 2m, OverReceiptTolerancePct = 5m }],
        });
        var lineId = po.Lines.Single().Id;
        var raise = new { description = "Resin", quantity = 100m, unitPrice = 2m, overTolerancePct = 9m };

        await ShouldBeRefusedAsync(
            await Client("Procurement").PutAsJsonAsync($"/api/v1/purchase-orders/{po.Id}/lines/{lineId}", raise));

        (await Client("Manager").PutAsJsonAsync($"/api/v1/purchase-orders/{po.Id}/lines/{lineId}", raise))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await InDbAsync(db => db.PurchaseOrderLines.AsNoTracking().Where(l => l.Id == lineId).Select(l => l.OverReceiptTolerancePct).SingleAsync()))
            .Should().Be(9m);
    }

    [Fact]
    public async Task A_sales_order_line_tolerance_is_raised_by_a_manager_and_not_by_the_pm()
    {
        var customer = await AddAsync(new Customer { Name = $"Raise {Guid.NewGuid():N}"[..20], OverShipTolerancePct = 4m });
        var order = await AddAsync(new SalesOrder
        {
            OrderNumber = $"SO-{Guid.NewGuid():N}"[..16], CustomerId = customer.Id, Status = SalesOrderStatus.Draft,
            Lines = [new SalesOrderLine { Description = "Bracket", Quantity = 100m, UnitPrice = 5m, LineNumber = 1 }],
        });
        var lineId = order.Lines.Single().Id;
        var raise = new { description = "Bracket", quantity = 100m, unitPrice = 5m, overTolerancePct = 6m };

        await ShouldBeRefusedAsync(
            await Client("PM").PutAsJsonAsync($"/api/v1/orders/{order.Id}/lines/{lineId}", raise));

        (await Client("Manager").PutAsJsonAsync($"/api/v1/orders/{order.Id}/lines/{lineId}", raise))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await InDbAsync(db => db.SalesOrderLines.AsNoTracking().Where(l => l.Id == lineId).Select(l => l.OverShipTolerancePct).SingleAsync()))
            .Should().Be(6m);
    }
}
