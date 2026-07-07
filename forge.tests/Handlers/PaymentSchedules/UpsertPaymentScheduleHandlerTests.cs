using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Forge.Api.Features.PaymentSchedules;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.PaymentSchedules;

public class UpsertPaymentScheduleHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly UpsertPaymentScheduleValidator _validator = new();

    private static IClock FixedClock()
    {
        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(Now);
        return clock.Object;
    }

    private static async Task<Quote> SeedQuoteAsync(AppDbContext db)
    {
        var customer = new Customer { Name = "Acme Corp" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var quote = new Quote
        {
            QuoteNumber = "QT-0001",
            CustomerId = customer.Id,
            Status = QuoteStatus.Draft,
            TaxRate = 0m,
        };
        quote.Lines.Add(new QuoteLine { Description = "Widget", Quantity = 4, UnitPrice = 250m, LineNumber = 1 });
        db.Quotes.Add(quote);
        await db.SaveChangesAsync();
        return quote;
    }

    [Fact]
    public void Validator_PercentagesSummingTo99Point9_IsRejected()
    {
        var command = new UpsertPaymentScheduleCommand(1,
        [
            new PaymentMilestoneRequestModel("Deposit", 49.9m, PaymentDueTrigger.OnAcceptance),
            new PaymentMilestoneRequestModel("Balance", 50m, PaymentDueTrigger.OnShipment),
        ]);

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("sum to exactly 100"));
    }

    [Fact]
    public void Validator_PercentagesSummingTo100_IsAccepted()
    {
        var command = new UpsertPaymentScheduleCommand(1,
        [
            new PaymentMilestoneRequestModel("Deposit", 30m, PaymentDueTrigger.OnAcceptance),
            new PaymentMilestoneRequestModel("Progress", 45.5m, PaymentDueTrigger.OnProductionStart),
            new PaymentMilestoneRequestModel("Balance", 24.5m, PaymentDueTrigger.NetDays, NetDays: 30),
        ]);

        _validator.Validate(command).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validator_EnforcesTriggerSpecificFields_AndLimits()
    {
        // FixedDate without DueDate + NetDays without NetDays + non-positive pct + missing name.
        var command = new UpsertPaymentScheduleCommand(1,
        [
            new PaymentMilestoneRequestModel("Fixed", 25m, PaymentDueTrigger.FixedDate),
            new PaymentMilestoneRequestModel("Net", 25m, PaymentDueTrigger.NetDays),
            new PaymentMilestoneRequestModel("", 50m, PaymentDueTrigger.OnAcceptance),
        ]);

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("FixedDate milestone requires a due date"));
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("NetDays milestone requires a net-days value"));

        var tooMany = new UpsertPaymentScheduleCommand(1,
            Enumerable.Range(1, 21)
                .Select(i => new PaymentMilestoneRequestModel($"M{i}", 100m / 21m, PaymentDueTrigger.OnAcceptance))
                .ToList());
        _validator.Validate(tooMany).Errors
            .Should().Contain(e => e.ErrorMessage.Contains("at most 20"));
    }

    [Fact]
    public async Task Handle_NoExistingSchedule_CreatesDraftScheduleWithSequencedMilestones()
    {
        using var db = TestDbContextFactory.Create();
        var quote = await SeedQuoteAsync(db);

        var handler = new UpsertPaymentScheduleHandler(db, FixedClock());
        var result = await handler.Handle(new UpsertPaymentScheduleCommand(quote.Id,
        [
            new PaymentMilestoneRequestModel("Deposit", 30m, PaymentDueTrigger.OnAcceptance),
            new PaymentMilestoneRequestModel("Balance", 70m, PaymentDueTrigger.OnShipment),
        ]), CancellationToken.None);

        result.Status.Should().Be("Draft");
        result.QuoteId.Should().Be(quote.Id);
        result.SalesOrderId.Should().BeNull();
        result.Milestones.Should().HaveCount(2);
        result.Milestones.Select(m => m.Sequence).Should().Equal(1, 2);
        result.Milestones.Select(m => m.Status).Should().AllBe("Pending"); // quote not accepted yet
        // Amounts derive from the quote total (1000).
        result.Milestones[0].AmountDue.Should().Be(300.00m);

        var schedule = await db.PaymentSchedules.Include(s => s.Milestones).SingleAsync();
        schedule.Status.Should().Be(PaymentScheduleStatus.Draft);
        schedule.Milestones.Should().HaveCount(2);

        // One rollup activity row on the quote.
        db.ActivityLogs.Where(a => a.EntityType == "Quote" && a.EntityId == quote.Id
            && a.Action == "payment-schedule-updated").Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_ExistingSchedule_ReplacesMilestonesAndSoftDeletesOldOnes()
    {
        using var db = TestDbContextFactory.Create();
        var quote = await SeedQuoteAsync(db);

        var schedule = new PaymentSchedule { QuoteId = quote.Id };
        schedule.Milestones.Add(new PaymentMilestone
        {
            Sequence = 1, Name = "Old deposit", Percentage = 50m, DueTrigger = PaymentDueTrigger.OnAcceptance,
        });
        schedule.Milestones.Add(new PaymentMilestone
        {
            Sequence = 2, Name = "Old balance", Percentage = 50m, DueTrigger = PaymentDueTrigger.OnDelivery,
        });
        db.PaymentSchedules.Add(schedule);
        await db.SaveChangesAsync();

        var handler = new UpsertPaymentScheduleHandler(db, FixedClock());
        var result = await handler.Handle(new UpsertPaymentScheduleCommand(quote.Id,
        [
            new PaymentMilestoneRequestModel("Deposit", 25m, PaymentDueTrigger.OnAcceptance),
            new PaymentMilestoneRequestModel("Progress", 25m, PaymentDueTrigger.OnProductionStart),
            new PaymentMilestoneRequestModel("Balance", 50m, PaymentDueTrigger.OnDelivery),
        ]), CancellationToken.None);

        result.Milestones.Should().HaveCount(3);

        db.PaymentSchedules.Count().Should().Be(1); // same schedule row — no clone
        var live = await db.PaymentMilestones.Where(m => m.PaymentScheduleId == schedule.Id).ToListAsync();
        live.Should().HaveCount(3);
        var all = await db.PaymentMilestones.IgnoreQueryFilters()
            .Where(m => m.PaymentScheduleId == schedule.Id).ToListAsync();
        all.Should().HaveCount(5); // 2 soft-deleted + 3 live
        all.Where(m => m.DeletedAt != null).Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_MilestoneWithLockedMoney_RejectsWholeUpsert()
    {
        using var db = TestDbContextFactory.Create();
        var quote = await SeedQuoteAsync(db);

        var schedule = new PaymentSchedule { QuoteId = quote.Id, Status = PaymentScheduleStatus.Active };
        schedule.Milestones.Add(new PaymentMilestone
        {
            Sequence = 1, Name = "Deposit", Percentage = 30m, DueTrigger = PaymentDueTrigger.OnAcceptance,
            Status = PaymentMilestoneStatus.Paid, AmountLocked = 300m, PaidAmount = 300m,
        });
        schedule.Milestones.Add(new PaymentMilestone
        {
            Sequence = 2, Name = "Balance", Percentage = 70m, DueTrigger = PaymentDueTrigger.OnDelivery,
        });
        db.PaymentSchedules.Add(schedule);
        await db.SaveChangesAsync();

        var handler = new UpsertPaymentScheduleHandler(db, FixedClock());
        var act = () => handler.Handle(new UpsertPaymentScheduleCommand(quote.Id,
        [
            new PaymentMilestoneRequestModel("Everything", 100m, PaymentDueTrigger.OnDelivery),
        ]), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already invoiced or paid*");
    }

    [Fact]
    public async Task Handle_UnknownQuote_Throws404()
    {
        using var db = TestDbContextFactory.Create();
        var handler = new UpsertPaymentScheduleHandler(db, FixedClock());

        var act = () => handler.Handle(new UpsertPaymentScheduleCommand(999,
        [
            new PaymentMilestoneRequestModel("All", 100m, PaymentDueTrigger.OnAcceptance),
        ]), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
