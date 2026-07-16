using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Features.PaymentSchedules;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.PaymentSchedules;

/// <summary>
/// The payment-schedule "advancer" (Phase C): auto-generates invoices for exactly the Pending
/// milestones whose trigger is now satisfied against the order's state, skipping not-yet-due and
/// already-invoiced milestones. This is the event plumbing the milestone MVP left out.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AdvancePaymentScheduleHandlerTests(PostgresFixture fixture)
{
    private static Mock<IClock> Clock()
    {
        var clock = new Mock<IClock>();
        clock.SetupGet(c => c.UtcNow).Returns(new DateTimeOffset(2021, 6, 1, 0, 0, 0, TimeSpan.Zero));
        return clock;
    }

    [Fact]
    public async Task Generates_invoices_only_for_due_uninvoiced_milestones()
    {
        await using var db = fixture.CreateContext();

        var customer = new Customer { Name = "Milestone Co" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        var so = new SalesOrder { CustomerId = customer.Id, OrderNumber = $"SO-{Guid.NewGuid():N}"[..12], Status = SalesOrderStatus.Confirmed };
        db.SalesOrders.Add(so);
        await db.SaveChangesAsync();

        var schedule = new PaymentSchedule { SalesOrderId = so.Id, Status = PaymentScheduleStatus.Active };
        db.PaymentSchedules.Add(schedule);
        await db.SaveChangesAsync();

        var mDue = new PaymentMilestone { PaymentScheduleId = schedule.Id, Sequence = 1, Name = "Deposit", Percentage = 50m, DueTrigger = PaymentDueTrigger.OnOrderConfirmation, Status = PaymentMilestoneStatus.Pending };
        var mNotDue = new PaymentMilestone { PaymentScheduleId = schedule.Id, Sequence = 2, Name = "Balance", Percentage = 50m, DueTrigger = PaymentDueTrigger.OnDelivery, Status = PaymentMilestoneStatus.Pending };
        var mAlready = new PaymentMilestone { PaymentScheduleId = schedule.Id, Sequence = 3, Name = "Already", Percentage = 0m, DueTrigger = PaymentDueTrigger.OnOrderConfirmation, Status = PaymentMilestoneStatus.Invoiced };
        db.PaymentMilestones.AddRange(mDue, mNotDue, mAlready);
        await db.SaveChangesAsync();

        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<GenerateMilestoneInvoiceCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((InvoiceListItemModel)null!);

        await new AdvancePaymentScheduleHandler(db, mediator.Object, Clock().Object, NullLogger<AdvancePaymentScheduleHandler>.Instance)
            .Handle(new AdvancePaymentScheduleCommand(so.Id), CancellationToken.None);

        // Only the due, un-invoiced OnOrderConfirmation milestone is invoiced.
        mediator.Verify(m => m.Send(It.Is<GenerateMilestoneInvoiceCommand>(c => c.MilestoneId == mDue.Id), It.IsAny<CancellationToken>()), Times.Once);
        mediator.Verify(m => m.Send(It.Is<GenerateMilestoneInvoiceCommand>(c => c.MilestoneId == mNotDue.Id), It.IsAny<CancellationToken>()), Times.Never);
        mediator.Verify(m => m.Send(It.Is<GenerateMilestoneInvoiceCommand>(c => c.MilestoneId == mAlready.Id), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task No_schedule_is_a_no_op()
    {
        await using var db = fixture.CreateContext();
        var customer = new Customer { Name = "No Schedule Co" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        var so = new SalesOrder { CustomerId = customer.Id, OrderNumber = $"SO-{Guid.NewGuid():N}"[..12], Status = SalesOrderStatus.Confirmed };
        db.SalesOrders.Add(so);
        await db.SaveChangesAsync();

        var mediator = new Mock<IMediator>();
        var result = await new AdvancePaymentScheduleHandler(db, mediator.Object, Clock().Object, NullLogger<AdvancePaymentScheduleHandler>.Instance)
            .Handle(new AdvancePaymentScheduleCommand(so.Id), CancellationToken.None);

        result.Should().BeEmpty();
        mediator.Verify(m => m.Send(It.IsAny<GenerateMilestoneInvoiceCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
