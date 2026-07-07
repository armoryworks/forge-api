using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Forge.Api.Features.Quotes;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.PaymentSchedules;

/// <summary>
/// S2 — conversion re-links (never clones) the quote's payment schedule onto the
/// new sales order. Deliberately separate from ConvertQuoteToOrderHandlerTests
/// (which exercises the handler with mocked repos); these run the real
/// repositories against an in-memory context so the post-save re-link is visible.
/// </summary>
public class ConvertQuoteToOrderPaymentScheduleTests
{
    private static async Task<Quote> SeedAcceptedQuoteAsync(AppDbContext db)
    {
        var customer = new Customer { Name = "Acme Corp" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var quote = new Quote
        {
            QuoteNumber = "QT-0001",
            CustomerId = customer.Id,
            Status = QuoteStatus.Accepted,
            AcceptedDate = DateTimeOffset.UtcNow,
            TaxRate = 0m,
        };
        quote.Lines.Add(new QuoteLine { Description = "Widget", Quantity = 10, UnitPrice = 100m, LineNumber = 1 });
        db.Quotes.Add(quote);
        await db.SaveChangesAsync();
        return quote;
    }

    private static ConvertQuoteToOrderHandler HandlerFor(AppDbContext db)
        => new(new QuoteRepository(db), new SalesOrderRepository(db), db);

    [Fact]
    public async Task Handle_QuoteWithSchedule_RelinksSameRowToNewOrderAndActivates()
    {
        using var db = TestDbContextFactory.Create();
        var quote = await SeedAcceptedQuoteAsync(db);

        var schedule = new PaymentSchedule { QuoteId = quote.Id, Status = PaymentScheduleStatus.Draft };
        schedule.Milestones.Add(new PaymentMilestone
        {
            Sequence = 1, Name = "Deposit", Percentage = 40m, DueTrigger = PaymentDueTrigger.OnAcceptance,
        });
        schedule.Milestones.Add(new PaymentMilestone
        {
            Sequence = 2, Name = "Balance", Percentage = 60m, DueTrigger = PaymentDueTrigger.OnShipment,
        });
        db.PaymentSchedules.Add(schedule);
        await db.SaveChangesAsync();

        var result = await HandlerFor(db).Handle(new ConvertQuoteToOrderCommand(quote.Id), CancellationToken.None);

        // Same row, re-linked and activated — never cloned.
        db.PaymentSchedules.Count().Should().Be(1);
        var relinked = await db.PaymentSchedules.Include(s => s.Milestones).SingleAsync();
        relinked.Id.Should().Be(schedule.Id);
        relinked.QuoteId.Should().Be(quote.Id);
        relinked.SalesOrderId.Should().Be(result.Id);
        relinked.Status.Should().Be(PaymentScheduleStatus.Active);
        relinked.Milestones.Should().HaveCount(2); // milestones untouched

        // Existing convert behavior is preserved.
        quote.Status.Should().Be(QuoteStatus.ConvertedToOrder);
        result.Total.Should().Be(1000m);

        db.ActivityLogs.Where(a => a.Action == "payment-schedule-activated")
            .Select(a => a.EntityType).Should().BeEquivalentTo("Quote", "SalesOrder");
    }

    [Fact]
    public async Task Handle_QuoteWithoutSchedule_ConvertsNormally()
    {
        using var db = TestDbContextFactory.Create();
        var quote = await SeedAcceptedQuoteAsync(db);

        var result = await HandlerFor(db).Handle(new ConvertQuoteToOrderCommand(quote.Id), CancellationToken.None);

        result.Id.Should().BeGreaterThan(0);
        quote.Status.Should().Be(QuoteStatus.ConvertedToOrder);
        db.PaymentSchedules.Count().Should().Be(0);
    }

    [Fact]
    public async Task Handle_CancelledSchedule_IsNotRelinked()
    {
        using var db = TestDbContextFactory.Create();
        var quote = await SeedAcceptedQuoteAsync(db);

        db.PaymentSchedules.Add(new PaymentSchedule
        {
            QuoteId = quote.Id,
            Status = PaymentScheduleStatus.Cancelled,
        });
        await db.SaveChangesAsync();

        var result = await HandlerFor(db).Handle(new ConvertQuoteToOrderCommand(quote.Id), CancellationToken.None);

        var schedule = db.PaymentSchedules.Single();
        schedule.SalesOrderId.Should().BeNull();
        schedule.Status.Should().Be(PaymentScheduleStatus.Cancelled);
        result.Id.Should().BeGreaterThan(0);
    }
}
