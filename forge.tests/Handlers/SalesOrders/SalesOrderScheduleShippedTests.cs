using FluentAssertions;

using Forge.Api.Features.SalesOrders;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.SalesOrders;

public class SalesOrderScheduleShippedTests
{
    [Fact]
    public async Task Fully_shipped_line_is_marked_shipped_and_never_at_risk()
    {
        using var db = TestDbContextFactory.Create();
        var customer = new Customer { Name = "Acme" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        var so = new SalesOrder
        {
            OrderNumber = "SO-1",
            CustomerId = customer.Id,
            Status = SalesOrderStatus.Shipped,
            RequestedDeliveryDate = DateTimeOffset.UtcNow.AddDays(-30),
        };
        so.Lines.Add(new SalesOrderLine { Description = "Shipped", Quantity = 5, ShippedQuantity = 5, UnitPrice = 1, LineNumber = 1 });
        so.Lines.Add(new SalesOrderLine { Description = "Open", Quantity = 5, ShippedQuantity = 2, UnitPrice = 1, LineNumber = 2 });
        db.SalesOrders.Add(so);
        await db.SaveChangesAsync();

        var clock = new SystemClock();
        var handler = new GetSalesOrderScheduleHandler(db, new BackwardSchedulingService(db, clock, new PartSourcingResolver(db)), clock);
        var milestones = await handler.Handle(new GetSalesOrderScheduleQuery(so.Id), CancellationToken.None);

        var shipped = milestones.Single(m => m.SalesOrderLineId == so.Lines.First(l => l.LineNumber == 1).Id);
        var open = milestones.Single(m => m.SalesOrderLineId == so.Lines.First(l => l.LineNumber == 2).Id);
        shipped.IsShipped.Should().BeTrue();
        shipped.IsAtRisk.Should().BeFalse();
        open.IsShipped.Should().BeFalse();
        open.IsAtRisk.Should().BeTrue();
    }
}
