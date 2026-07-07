using FluentAssertions;
using MediatR;
using Moq;
using Forge.Api.Features.Invoices;
using Forge.Api.Features.PaymentSchedules;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.PaymentSchedules;

public class GenerateMilestoneInvoiceHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IMediator> _mediator = new();
    private readonly Mock<IAccountingService> _accounting = new();

    private static IClock FixedClock()
    {
        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(Now);
        return clock.Object;
    }

    /// <summary>SO total 1000; schedule linked to it; one 30% milestone (worth 300).</summary>
    private static async Task<(PaymentMilestone milestone, SalesOrder order)> SeedLinkedMilestoneAsync(AppDbContext db)
    {
        var customer = new Customer { Name = "Acme Corp" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var order = new SalesOrder
        {
            OrderNumber = "SO-1001",
            CustomerId = customer.Id,
            Status = SalesOrderStatus.Confirmed,
            TaxRate = 0m,
        };
        order.Lines.Add(new SalesOrderLine { Description = "Widget", Quantity = 10, UnitPrice = 100m, LineNumber = 1 });
        db.SalesOrders.Add(order);
        await db.SaveChangesAsync();

        var schedule = new PaymentSchedule { SalesOrderId = order.Id, Status = PaymentScheduleStatus.Active };
        var milestone = new PaymentMilestone
        {
            Sequence = 1, Name = "Deposit", Percentage = 30m, DueTrigger = PaymentDueTrigger.OnOrderConfirmation,
        };
        schedule.Milestones.Add(milestone);
        db.PaymentSchedules.Add(schedule);
        await db.SaveChangesAsync();
        return (milestone, order);
    }

    [Fact]
    public async Task Handle_IntegratedMode_ThrowsAndCreatesNothing()
    {
        using var db = TestDbContextFactory.Create();
        var (milestone, _) = await SeedLinkedMilestoneAsync(db);

        // ⚡ Accounting boundary: connected provider → the feature must refuse.
        _accounting.Setup(a => a.TestConnectionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var handler = new GenerateMilestoneInvoiceHandler(db, _mediator.Object, FixedClock(), _accounting.Object);

        var act = () => handler.Handle(new GenerateMilestoneInvoiceCommand(milestone.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*standalone*");
        _mediator.Verify(m => m.Send(It.IsAny<CreateInvoiceCommand>(), It.IsAny<CancellationToken>()), Times.Never);
        milestone.InvoiceId.Should().BeNull();
        milestone.Status.Should().Be(PaymentMilestoneStatus.Pending);
    }

    [Fact]
    public async Task Handle_StandaloneMode_CreatesOneLineInvoiceForDerivedAmount()
    {
        using var db = TestDbContextFactory.Create();
        var (milestone, order) = await SeedLinkedMilestoneAsync(db);

        // Standalone: the registered provider shim reports no live connection.
        _accounting.Setup(a => a.TestConnectionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
        CreateInvoiceCommand? sent = null;
        _mediator
            .Setup(m => m.Send(It.IsAny<CreateInvoiceCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<InvoiceListItemModel>, CancellationToken>((cmd, _) => sent = (CreateInvoiceCommand)cmd)
            .ReturnsAsync(new InvoiceListItemModel(
                77, "INV-00077", order.CustomerId, "Acme Corp", "Draft",
                Now, Now, 300m, 0m, 300m, Now));

        var handler = new GenerateMilestoneInvoiceHandler(db, _mediator.Object, FixedClock(), _accounting.Object);
        var result = await handler.Handle(new GenerateMilestoneInvoiceCommand(milestone.Id), CancellationToken.None);

        result.Id.Should().Be(77);

        sent.Should().NotBeNull();
        sent!.CustomerId.Should().Be(order.CustomerId);
        sent.SalesOrderId.Should().Be(order.Id);
        sent.TaxRate.Should().Be(0m);
        sent.Lines.Should().HaveCount(1);
        sent.Lines[0].PartId.Should().BeNull();
        sent.Lines[0].Quantity.Should().Be(1m);
        sent.Lines[0].UnitPrice.Should().Be(300.00m); // 30% of the SO total (1000)
        sent.Lines[0].Description.Should().Be("Payment milestone 1: Deposit (30%)");

        milestone.InvoiceId.Should().Be(77);
        milestone.AmountLocked.Should().Be(300.00m);
        milestone.Status.Should().Be(PaymentMilestoneStatus.Invoiced);
    }

    [Fact]
    public async Task Handle_NullAccountingService_BehavesAsStandalone()
    {
        using var db = TestDbContextFactory.Create();
        var (milestone, order) = await SeedLinkedMilestoneAsync(db);

        _mediator
            .Setup(m => m.Send(It.IsAny<CreateInvoiceCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InvoiceListItemModel(
                5, "INV-00005", order.CustomerId, "Acme Corp", "Draft",
                Now, Now, 300m, 0m, 300m, Now));

        var handler = new GenerateMilestoneInvoiceHandler(db, _mediator.Object, FixedClock());
        var result = await handler.Handle(new GenerateMilestoneInvoiceCommand(milestone.Id), CancellationToken.None);

        result.Id.Should().Be(5);
        milestone.Status.Should().Be(PaymentMilestoneStatus.Invoiced);
    }

    [Fact]
    public async Task Handle_ScheduleNotLinkedToOrder_Rejects()
    {
        using var db = TestDbContextFactory.Create();
        var quote = new Quote { QuoteNumber = "QT-0001", CustomerId = 1, TaxRate = 0m };
        db.Quotes.Add(quote);
        await db.SaveChangesAsync();

        var schedule = new PaymentSchedule { QuoteId = quote.Id };
        var milestone = new PaymentMilestone
        {
            Sequence = 1, Name = "Deposit", Percentage = 30m, DueTrigger = PaymentDueTrigger.OnAcceptance,
        };
        schedule.Milestones.Add(milestone);
        db.PaymentSchedules.Add(schedule);
        await db.SaveChangesAsync();

        var handler = new GenerateMilestoneInvoiceHandler(db, _mediator.Object, FixedClock());
        var act = () => handler.Handle(new GenerateMilestoneInvoiceCommand(milestone.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not linked to a sales order*");
    }

    [Fact]
    public async Task Handle_AlreadyInvoicedMilestone_Rejects()
    {
        using var db = TestDbContextFactory.Create();
        var (milestone, _) = await SeedLinkedMilestoneAsync(db);
        milestone.Status = PaymentMilestoneStatus.Invoiced;
        milestone.InvoiceId = 42;
        await db.SaveChangesAsync();

        var handler = new GenerateMilestoneInvoiceHandler(db, _mediator.Object, FixedClock());
        var act = () => handler.Handle(new GenerateMilestoneInvoiceCommand(milestone.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already been invoiced*");
    }
}
