using System.Net;
using System.Net.Http.Json;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Forge.Api.Features.CustomerReturns;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Capabilities;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.CustomerReturns;

[Collection(CapabilityTestCollection.Name)]
public class ChannelReturnOrdersTests(CapabilityTestWebApplicationFactory factory)
{
    private HttpClient Client(string role)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "1");
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    private static async Task<SalesOrder> SeedChannelOrderAsync(AppDbContext db, string externalNumber)
    {
        var customer = new Customer { Name = $"House {Guid.NewGuid():N}"[..16] };
        var channel = new SalesChannel { Name = "Marketplace", Code = $"MK{Guid.NewGuid():N}"[..10] };
        db.AddRange(customer, channel);
        await db.SaveChangesAsync();
        var buyer = new RetailBuyer { ChannelId = channel.Id, ExternalBuyerId = "B-1", DisplayName = "Jordan Buyer" };
        db.Add(buyer);
        await db.SaveChangesAsync();
        var order = new SalesOrder
        {
            OrderNumber = $"SO-{Guid.NewGuid():N}"[..12],
            CustomerId = customer.Id,
            ChannelId = channel.Id,
            RetailBuyerId = buyer.Id,
            ExternalOrderNumber = externalNumber,
            Status = SalesOrderStatus.Shipped,
            Lines =
            {
                new SalesOrderLine { Description = "Widget", Quantity = 2m, ShippedQuantity = 2m, UnitPrice = 9m, LineNumber = 1 },
                new SalesOrderLine { Description = "Shipping", Quantity = 1m, UnitPrice = 5m, LineNumber = 2, LineType = DocumentLineType.Freight },
            },
        };
        db.SalesOrders.Add(order);
        db.SalesOrders.Add(new SalesOrder { OrderNumber = $"SO-{Guid.NewGuid():N}"[..12], CustomerId = customer.Id });
        await db.SaveChangesAsync();
        return order;
    }

    [Fact]
    public async Task Lists_channel_orders_with_buyer_and_returnable_lines()
    {
        using var db = TestDbContextFactory.Create();
        var order = await SeedChannelOrderAsync(db, "AMZ-111-222");

        var rows = await new GetChannelReturnOrdersHandler(db).Handle(new GetChannelReturnOrdersQuery("amz-111"), CancellationToken.None);

        var row = rows.Should().ContainSingle().Subject;
        row.SalesOrderId.Should().Be(order.Id);
        row.ChannelName.Should().Be("Marketplace");
        row.BuyerName.Should().Be("Jordan Buyer");
        row.ExternalOrderNumber.Should().Be("AMZ-111-222");
        row.Lines.Should().ContainSingle().Which.Description.Should().Be("Widget");
    }

    [Fact]
    public async Task Orders_off_a_channel_are_not_offered()
    {
        using var db = TestDbContextFactory.Create();
        await SeedChannelOrderAsync(db, "EB-1");

        var rows = await new GetChannelReturnOrdersHandler(db).Handle(new GetChannelReturnOrdersQuery(null), CancellationToken.None);

        rows.Should().OnlyContain(r => r.ExternalOrderNumber == "EB-1");
    }

    private async Task<bool> EnabledAsync(string code)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.Capabilities.AsNoTracking().SingleAsync(c => c.Code == code)).Enabled;
    }

    [Fact]
    public async Task Return_roles_read_channel_orders_and_others_are_refused()
    {
        var admin = Client("Admin");
        var codes = new[] { "CAP-O2C-CHANNELS", "CAP-O2C-RETAIL" };
        var was = new Dictionary<string, bool>();
        foreach (var code in codes) was[code] = await EnabledAsync(code);
        foreach (var code in codes)
            await admin.PutAsync($"/api/v1/capabilities/{code}/enabled", JsonContent.Create(new { enabled = true }));
        try
        {
            var allowed = await Client("OfficeManager").GetAsync("/api/v1/customer-returns/channel/orders?q=zz");
            var refused = await Client("PM").GetAsync("/api/v1/customer-returns/channel/orders?q=zz");

            allowed.StatusCode.Should().Be(HttpStatusCode.OK);
            (await allowed.Content.ReadFromJsonAsync<List<ChannelReturnOrderModel>>()).Should().NotBeNull();
            refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        finally
        {
            foreach (var code in codes.Reverse().Where(c => !was[c]))
                await admin.PutAsync($"/api/v1/capabilities/{code}/enabled", JsonContent.Create(new { enabled = false }));
        }
    }

    [Fact]
    public async Task Channel_orders_are_hidden_when_retail_is_off()
    {
        if (await EnabledAsync("CAP-O2C-RETAIL")) return;

        var response = await Client("Admin").GetAsync("/api/v1/customer-returns/channel/orders");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
