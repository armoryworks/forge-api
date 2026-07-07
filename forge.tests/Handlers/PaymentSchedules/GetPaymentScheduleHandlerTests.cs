using FluentAssertions;
using Moq;
using Forge.Api.Features.PaymentSchedules;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.PaymentSchedules;

public class GetPaymentScheduleHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 7, 12, 0, 0, TimeSpan.Zero);

    private static IClock FixedClock()
    {
        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(Now);
        return clock.Object;
    }

    /// <summary>Quote with two lines totaling 1000 (no tax), Accepted.</summary>
    private static async Task<Quote> SeedQuoteAsync(AppDbContext db)
    {
        var customer = new Customer { Name = "Acme Corp" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var quote = new Quote
        {
            QuoteNumber = "QT-0001",
            CustomerId = customer.Id,
            Status = QuoteStatus.Accepted,
            AcceptedDate = Now.AddDays(-10),
            TaxRate = 0m,
        };
        quote.Lines.Add(new QuoteLine { Description = "Widget A", Quantity = 10, UnitPrice = 25m, LineNumber = 1 });
        quote.Lines.Add(new QuoteLine { Description = "Widget B", Quantity = 5, UnitPrice = 150m, LineNumber = 2 });
        db.Quotes.Add(quote);
        await db.SaveChangesAsync();
        return quote;
    }

    [Fact]
    public async Task Handle_ByQuoteId_DerivesAmountsFromQuoteTotal()
    {
        using var db = TestDbContextFactory.Create();
        var quote = await SeedQuoteAsync(db);

        var schedule = new PaymentSchedule { QuoteId = quote.Id };
        schedule.Milestones.Add(new PaymentMilestone
        {
            Sequence = 1, Name = "Deposit", Percentage = 30m, DueTrigger = PaymentDueTrigger.OnAcceptance,
        });
        schedule.Milestones.Add(new PaymentMilestone
        {
            Sequence = 2, Name = "Balance", Percentage = 70m, DueTrigger = PaymentDueTrigger.OnShipment,
        });
        db.PaymentSchedules.Add(schedule);
        await db.SaveChangesAsync();

        var handler = new GetPaymentScheduleHandler(db, FixedClock());
        var result = await handler.Handle(new GetPaymentScheduleQuery(quote.Id, null), CancellationToken.None);

        result.Should().NotBeNull();
        result!.QuoteId.Should().Be(quote.Id);
        result.Milestones.Should().HaveCount(2);
        result.Milestones[0].AmountDue.Should().Be(300.00m);
        result.Milestones[1].AmountDue.Should().Be(700.00m);
        result.Totals.DocumentTotal.Should().Be(1000.00m);
        result.Totals.PaidTotal.Should().Be(0m);
        result.Totals.RemainingTotal.Should().Be(1000.00m);
    }

    [Fact]
    public async Task Handle_ComputesEffectiveDueStatus_FromTriggerState()
    {
        using var db = TestDbContextFactory.Create();
        var quote = await SeedQuoteAsync(db);

        var schedule = new PaymentSchedule { QuoteId = quote.Id };
        // Quote is Accepted → OnAcceptance is due; no SO exists → OnShipment stays pending.
        schedule.Milestones.Add(new PaymentMilestone
        {
            Sequence = 1, Name = "Deposit", Percentage = 50m, DueTrigger = PaymentDueTrigger.OnAcceptance,
        });
        schedule.Milestones.Add(new PaymentMilestone
        {
            Sequence = 2, Name = "Balance", Percentage = 50m, DueTrigger = PaymentDueTrigger.OnShipment,
        });
        db.PaymentSchedules.Add(schedule);
        await db.SaveChangesAsync();

        var handler = new GetPaymentScheduleHandler(db, FixedClock());
        var result = await handler.Handle(new GetPaymentScheduleQuery(quote.Id, null), CancellationToken.None);

        result!.Milestones[0].Status.Should().Be("Due");
        result.Milestones[1].Status.Should().Be("Pending");
    }

    [Fact]
    public async Task Handle_LockedAmount_WinsOverDerivedAmount()
    {
        using var db = TestDbContextFactory.Create();
        var quote = await SeedQuoteAsync(db);

        var schedule = new PaymentSchedule { QuoteId = quote.Id };
        schedule.Milestones.Add(new PaymentMilestone
        {
            Sequence = 1, Name = "Deposit", Percentage = 30m,
            DueTrigger = PaymentDueTrigger.OnAcceptance,
            // Locked when the quote total was different — must NOT track the live total.
            AmountLocked = 123.45m,
            Status = PaymentMilestoneStatus.Invoiced,
        });
        schedule.Milestones.Add(new PaymentMilestone
        {
            Sequence = 2, Name = "Balance", Percentage = 70m, DueTrigger = PaymentDueTrigger.OnDelivery,
        });
        db.PaymentSchedules.Add(schedule);
        await db.SaveChangesAsync();

        var handler = new GetPaymentScheduleHandler(db, FixedClock());
        var result = await handler.Handle(new GetPaymentScheduleQuery(quote.Id, null), CancellationToken.None);

        result!.Milestones[0].AmountDue.Should().Be(123.45m);
        result.Milestones[0].Status.Should().Be("Invoiced");
        result.Milestones[1].AmountDue.Should().Be(700.00m);
    }

    [Fact]
    public async Task Handle_BySalesOrderId_UsesOrderTotalOnceLinked()
    {
        using var db = TestDbContextFactory.Create();
        var quote = await SeedQuoteAsync(db);

        var order = new SalesOrder
        {
            OrderNumber = "SO-1001",
            CustomerId = quote.CustomerId,
            QuoteId = quote.Id,
            Status = SalesOrderStatus.Confirmed,
            ConfirmedDate = Now.AddDays(-5),
            TaxRate = 0m,
        };
        // Order total (2000) deliberately differs from the quote total (1000).
        order.Lines.Add(new SalesOrderLine { Description = "Widget A", Quantity = 10, UnitPrice = 200m, LineNumber = 1 });
        db.SalesOrders.Add(order);
        await db.SaveChangesAsync();

        var schedule = new PaymentSchedule
        {
            QuoteId = quote.Id,
            SalesOrderId = order.Id,
            Status = PaymentScheduleStatus.Active,
        };
        schedule.Milestones.Add(new PaymentMilestone
        {
            Sequence = 1, Name = "On confirmation", Percentage = 25m, DueTrigger = PaymentDueTrigger.OnOrderConfirmation,
        });
        schedule.Milestones.Add(new PaymentMilestone
        {
            Sequence = 2, Name = "On delivery", Percentage = 75m, DueTrigger = PaymentDueTrigger.OnDelivery,
        });
        db.PaymentSchedules.Add(schedule);
        await db.SaveChangesAsync();

        var handler = new GetPaymentScheduleHandler(db, FixedClock());
        var result = await handler.Handle(new GetPaymentScheduleQuery(null, order.Id), CancellationToken.None);

        result.Should().NotBeNull();
        result!.SalesOrderId.Should().Be(order.Id);
        result.Totals.DocumentTotal.Should().Be(2000.00m);
        result.Milestones[0].AmountDue.Should().Be(500.00m);
        result.Milestones[0].Status.Should().Be("Due");     // SO is Confirmed
        result.Milestones[1].Status.Should().Be("Pending"); // not Completed yet
    }

    [Fact]
    public async Task Handle_NoScheduleForDocument_ReturnsNull()
    {
        using var db = TestDbContextFactory.Create();
        var quote = await SeedQuoteAsync(db);

        var handler = new GetPaymentScheduleHandler(db, FixedClock());
        var result = await handler.Handle(new GetPaymentScheduleQuery(quote.Id, null), CancellationToken.None);

        result.Should().BeNull();
    }
}
