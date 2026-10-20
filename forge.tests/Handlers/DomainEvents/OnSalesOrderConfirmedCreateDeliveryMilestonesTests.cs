using System.Globalization;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Forge.Api.Features.DomainEvents;
using Forge.Api.Features.DomainEvents.Handlers;
using Forge.Core.Entities;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.DomainEvents;

public class OnSalesOrderConfirmedCreateDeliveryMilestonesTests
{
    [Fact]
    public async Task Milestone_description_shows_the_quantity_without_trailing_zeros_whatever_the_server_culture()
    {
        using var db = TestDbContextFactory.Create();
        var so = new SalesOrder
        {
            OrderNumber = "SO-1001",
            CustomerId = 1,
            RequestedDeliveryDate = new DateTimeOffset(2026, 10, 30, 0, 0, 0, TimeSpan.Zero),
            Lines =
            [
                new SalesOrderLine { LineNumber = 1, Description = "Bracket", Quantity = 1500.5000m },
                new SalesOrderLine { LineNumber = 2, Description = "Cover", Quantity = 12.0000m },
            ],
        };
        db.SalesOrders.Add(so);
        await db.SaveChangesAsync();
        var handler = new OnSalesOrderConfirmed_CreateDeliveryMilestones(
            db, NullLogger<OnSalesOrderConfirmed_CreateDeliveryMilestones>.Instance);

        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        try
        {
            await handler.Handle(new SalesOrderConfirmedEvent(so.Id, 1), CancellationToken.None);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }

        var descriptions = await db.Events.OrderBy(e => e.Title).Select(e => e.Description).ToListAsync();
        descriptions.Should().Equal(
            "Delivery milestone for Sales Order SO-1001, Line 1. Quantity: 1,500.5.",
            "Delivery milestone for Sales Order SO-1001, Line 2. Quantity: 12.");
    }
}
