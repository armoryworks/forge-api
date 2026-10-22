using FluentAssertions;
using Moq;
using Forge.Api.Features.PaymentSchedules;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.PaymentSchedules;

public class MarkMilestonePaidHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 7, 12, 0, 0, TimeSpan.Zero);

    private static IClock FixedClock()
    {
        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(Now);
        return clock.Object;
    }

    /// <summary>Quote total 1000; one 30% milestone (worth 300).</summary>
    private static async Task<PaymentMilestone> SeedMilestoneAsync(AppDbContext db)
    {
        var customer = new Customer { Name = "Acme Corp" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var quote = new Quote
        {
            QuoteNumber = "QT-0001",
            CustomerId = customer.Id,
            Status = QuoteStatus.Accepted,
            TaxRate = 0m,
        };
        quote.Lines.Add(new QuoteLine { Description = "Widget", Quantity = 10, UnitPrice = 100m, LineNumber = 1 });
        db.Quotes.Add(quote);
        await db.SaveChangesAsync();

        var schedule = new PaymentSchedule { QuoteId = quote.Id, Status = PaymentScheduleStatus.Active };
        var milestone = new PaymentMilestone
        {
            Sequence = 1, Name = "Deposit", Percentage = 30m, DueTrigger = PaymentDueTrigger.OnAcceptance,
        };
        schedule.Milestones.Add(milestone);
        db.PaymentSchedules.Add(schedule);
        await db.SaveChangesAsync();
        return milestone;
    }

    [Fact]
    public async Task Handle_PartialPayment_AccumulatesLocksAmountAndSetsPartiallyPaid()
    {
        using var db = TestDbContextFactory.Create();
        var milestone = await SeedMilestoneAsync(db);
        var handler = new MarkMilestonePaidHandler(db, FixedClock());

        var result = await handler.Handle(
            new MarkMilestonePaidCommand(milestone.Id, 100m, "CHK-1001"), CancellationToken.None);

        result.Status.Should().Be("PartiallyPaid");
        result.PaidAmount.Should().Be(100m);
        result.AmountDue.Should().Be(300.00m);

        milestone.Status.Should().Be(PaymentMilestoneStatus.PartiallyPaid);
        milestone.PaidAmount.Should().Be(100m);
        milestone.PaidAt.Should().Be(Now);
        milestone.PaidReference.Should().Be("CHK-1001");
        // First payment locks the derived amount so later quote edits can't move it.
        milestone.AmountLocked.Should().Be(300.00m);
    }

    [Fact]
    public async Task Handle_PaymentsAccumulateToFullAmount_FlipsToPaid()
    {
        using var db = TestDbContextFactory.Create();
        var milestone = await SeedMilestoneAsync(db);
        var handler = new MarkMilestonePaidHandler(db, FixedClock());

        await handler.Handle(new MarkMilestonePaidCommand(milestone.Id, 100m, "CHK-1001"), CancellationToken.None);
        var result = await handler.Handle(
            new MarkMilestonePaidCommand(milestone.Id, 200m, "CHK-1002"), CancellationToken.None);

        result.Status.Should().Be("Paid");
        result.PaidAmount.Should().Be(300m);
        milestone.Status.Should().Be(PaymentMilestoneStatus.Paid);
        milestone.PaidReference.Should().Be("CHK-1002");
    }

    [Fact]
    public async Task Handle_LockedAmountIsNotRederived_WhenDocumentTotalChanges()
    {
        using var db = TestDbContextFactory.Create();
        var milestone = await SeedMilestoneAsync(db);
        var handler = new MarkMilestonePaidHandler(db, FixedClock());

        await handler.Handle(new MarkMilestonePaidCommand(milestone.Id, 100m, null), CancellationToken.None);

        // Quote grows after the first payment — the milestone's worth must stay 300.
        var quote = db.Quotes.Single();
        db.QuoteLines.Add(new QuoteLine
        {
            QuoteId = quote.Id, Description = "Extra", Quantity = 10, UnitPrice = 100m, LineNumber = 2,
        });
        await db.SaveChangesAsync();

        var result = await handler.Handle(
            new MarkMilestonePaidCommand(milestone.Id, 200m, null), CancellationToken.None);

        result.AmountDue.Should().Be(300.00m);
        result.Status.Should().Be("Paid");
    }

    [Fact]
    public async Task Handle_WaivedOrPaidMilestone_Rejects()
    {
        using var db = TestDbContextFactory.Create();
        var milestone = await SeedMilestoneAsync(db);
        var handler = new MarkMilestonePaidHandler(db, FixedClock());

        milestone.Status = PaymentMilestoneStatus.Waived;
        await db.SaveChangesAsync();
        var actWaived = () => handler.Handle(new MarkMilestonePaidCommand(milestone.Id, 50m, null), CancellationToken.None);
        await actWaived.Should().ThrowAsync<InvalidOperationException>().WithMessage("*waived*");

        milestone.Status = PaymentMilestoneStatus.Paid;
        await db.SaveChangesAsync();
        var actPaid = () => handler.Handle(new MarkMilestonePaidCommand(milestone.Id, 50m, null), CancellationToken.None);
        await actPaid.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already fully paid*");
    }

    [Fact]
    public async Task Handle_PaymentAboveRemaining_RejectsWithoutChangingTheMilestone()
    {
        using var db = TestDbContextFactory.Create();
        var milestone = await SeedMilestoneAsync(db);
        var handler = new MarkMilestonePaidHandler(db, FixedClock());

        await handler.Handle(new MarkMilestonePaidCommand(milestone.Id, 100m, "CHK-1001"), CancellationToken.None);

        var act = () => handler.Handle(
            new MarkMilestonePaidCommand(milestone.Id, 200.01m, "CHK-1002"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Payment of 200.01 exceeds the 200.00 still due on milestone 1");
        milestone.PaidAmount.Should().Be(100m);
        milestone.Status.Should().Be(PaymentMilestoneStatus.PartiallyPaid);
        milestone.PaidReference.Should().Be("CHK-1001");
    }

    [Fact]
    public async Task Handle_FirstPaymentAboveAmountDue_RejectsAndLeavesAmountUnlocked()
    {
        using var db = TestDbContextFactory.Create();
        var milestone = await SeedMilestoneAsync(db);
        var handler = new MarkMilestonePaidHandler(db, FixedClock());

        var act = () => handler.Handle(
            new MarkMilestonePaidCommand(milestone.Id, 99999.99m, null), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*exceeds the 300.00 still due*");
        milestone.AmountLocked.Should().BeNull();
        milestone.PaidAmount.Should().BeNull();
    }

    [Theory]
    [InlineData(99999.999)]
    [InlineData(0.001)]
    public void Validator_RejectsMoreThanTwoDecimals(decimal amount)
    {
        var result = new MarkMilestonePaidValidator().Validate(new MarkMilestonePaidCommand(1, amount, null));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(MarkMilestonePaidCommand.PaidAmount)
            && e.ErrorMessage.Contains("at most 2 decimal places"));
    }

    [Fact]
    public void Validator_AcceptsTrailingZerosBeyondTwoDecimals()
    {
        new MarkMilestonePaidValidator().Validate(new MarkMilestonePaidCommand(1, 10.500m, null))
            .IsValid.Should().BeTrue();
    }
}
