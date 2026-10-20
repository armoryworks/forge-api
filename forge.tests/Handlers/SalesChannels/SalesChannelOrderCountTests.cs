using FluentAssertions;

using Forge.Api.Features.SalesChannels;
using Forge.Core.Entities;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.SalesChannels;

public class SalesChannelOrderCountTests
{
    [Fact]
    public async Task Default_channel_counts_orders_with_no_channel()
    {
        using var db = TestDbContextFactory.Create();
        var customer = new Customer { Name = "Acme" };
        var direct = new SalesChannel { Name = "Direct", Code = "DIRECT", IsDefault = true };
        var web = new SalesChannel { Name = "Web store", Code = "WEB" };
        db.AddRange(customer, direct, web);
        await db.SaveChangesAsync();
        db.SalesOrders.AddRange(
            new SalesOrder { OrderNumber = "SO-1", CustomerId = customer.Id },
            new SalesOrder { OrderNumber = "SO-2", CustomerId = customer.Id },
            new SalesOrder { OrderNumber = "SO-3", CustomerId = customer.Id, ChannelId = direct.Id },
            new SalesOrder { OrderNumber = "SO-4", CustomerId = customer.Id, ChannelId = web.Id });
        await db.SaveChangesAsync();

        var channels = await new GetSalesChannelsHandler(db).Handle(new GetSalesChannelsQuery(), CancellationToken.None);
        var single = await GetSalesChannelById.ProjectAsync(db, direct.Id, CancellationToken.None);

        channels.Single(c => c.Id == direct.Id).OrderCount.Should().Be(3);
        channels.Single(c => c.Id == web.Id).OrderCount.Should().Be(1);
        single.OrderCount.Should().Be(3);
    }
}
