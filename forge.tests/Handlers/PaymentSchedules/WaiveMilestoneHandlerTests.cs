using FluentAssertions;
using Forge.Api.Features.PaymentSchedules;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.PaymentSchedules;

public class WaiveMilestoneHandlerTests
{
    private static async Task<PaymentMilestone> SeedMilestoneAsync(AppDbContext db, PaymentMilestoneStatus status)
    {
        var quote = new Quote { QuoteNumber = "QT-0001", CustomerId = 1, TaxRate = 0m };
        db.Quotes.Add(quote);
        await db.SaveChangesAsync();

        var schedule = new PaymentSchedule { QuoteId = quote.Id, Status = PaymentScheduleStatus.Active };
        var milestone = new PaymentMilestone
        {
            Sequence = 1, Name = "Deposit", Percentage = 30m,
            DueTrigger = PaymentDueTrigger.OnAcceptance, Status = status,
        };
        schedule.Milestones.Add(milestone);
        db.PaymentSchedules.Add(schedule);
        await db.SaveChangesAsync();
        return milestone;
    }

    [Fact]
    public async Task Handle_PendingMilestone_IsWaivedAndLogged()
    {
        using var db = TestDbContextFactory.Create();
        var milestone = await SeedMilestoneAsync(db, PaymentMilestoneStatus.Pending);
        var handler = new WaiveMilestoneHandler(db);

        await handler.Handle(new WaiveMilestoneCommand(milestone.Id), CancellationToken.None);

        milestone.Status.Should().Be(PaymentMilestoneStatus.Waived);
        db.ActivityLogs.Where(a => a.Action == "payment-milestone-waived").Should().HaveCount(1);
    }

    [Theory]
    [InlineData(PaymentMilestoneStatus.Paid)]
    [InlineData(PaymentMilestoneStatus.PartiallyPaid)]
    public async Task Handle_MilestoneWithRecordedPayments_CannotBeWaived(PaymentMilestoneStatus status)
    {
        using var db = TestDbContextFactory.Create();
        var milestone = await SeedMilestoneAsync(db, status);
        var handler = new WaiveMilestoneHandler(db);

        var act = () => handler.Handle(new WaiveMilestoneCommand(milestone.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*recorded payments*");
        milestone.Status.Should().Be(status);
    }

    [Fact]
    public async Task Handle_UnknownMilestone_Throws404()
    {
        using var db = TestDbContextFactory.Create();
        var handler = new WaiveMilestoneHandler(db);

        var act = () => handler.Handle(new WaiveMilestoneCommand(999), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
