using FluentAssertions;
using Moq;

using Forge.Api.Features.Payments;
using Forge.Api.Features.PaymentSchedules;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Core.Settings;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.PaymentSchedules;

public class InvoicedMilestonePaymentTests
{
    private static readonly DateTimeOffset PaidOn = new(2026, 7, 10, 0, 0, 0, TimeSpan.Zero);

    private static async Task<(PaymentMilestone milestone, Invoice invoice)> SeedInvoicedMilestoneAsync(AppDbContext db)
    {
        var customer = new Customer { Name = "Acme Corp" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var order = new SalesOrder { OrderNumber = "SO-1001", CustomerId = customer.Id, Status = SalesOrderStatus.Confirmed };
        order.Lines.Add(new SalesOrderLine { Description = "Widget", Quantity = 10, UnitPrice = 100m, LineNumber = 1 });
        db.SalesOrders.Add(order);
        await db.SaveChangesAsync();

        var invoice = new Invoice
        {
            InvoiceNumber = "INV-00077",
            CustomerId = customer.Id,
            SalesOrderId = order.Id,
            Status = InvoiceStatus.Sent,
            InvoiceDate = PaidOn,
            DueDate = PaidOn,
        };
        invoice.Lines.Add(new InvoiceLine { Description = "Payment milestone 1: Deposit (30%)", Quantity = 1m, UnitPrice = 300m, LineNumber = 1 });
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        var schedule = new PaymentSchedule { SalesOrderId = order.Id, Status = PaymentScheduleStatus.Active };
        var milestone = new PaymentMilestone
        {
            Sequence = 1, Name = "Deposit", Percentage = 30m, DueTrigger = PaymentDueTrigger.OnOrderConfirmation,
            Status = PaymentMilestoneStatus.Invoiced, InvoiceId = invoice.Id, AmountLocked = 300m,
        };
        schedule.Milestones.Add(milestone);
        db.PaymentSchedules.Add(schedule);
        await db.SaveChangesAsync();
        return (milestone, invoice);
    }

    private static CreatePaymentHandler PaymentHandler(AppDbContext db) =>
        new(new PaymentRepository(db), new CustomerRepository(db), new InvoiceRepository(db), db);

    private static Task<PaymentListItemModel> PayAsync(AppDbContext db, Invoice invoice, decimal amount) =>
        PaymentHandler(db).Handle(
            new CreatePaymentCommand(invoice.CustomerId, "Check", amount, PaidOn, "CHK-1", null,
                [new CreatePaymentApplicationModel(invoice.Id, amount)]),
            CancellationToken.None);

    [Fact]
    public async Task Paying_the_milestone_invoice_in_full_marks_the_milestone_paid()
    {
        using var db = TestDbContextFactory.Create();
        var (milestone, invoice) = await SeedInvoicedMilestoneAsync(db);

        var payment = await PayAsync(db, invoice, 300m);

        milestone.Status.Should().Be(PaymentMilestoneStatus.Paid);
        milestone.PaidAmount.Should().Be(300m);
        milestone.PaidAt.Should().Be(PaidOn);
        milestone.PaidReference.Should().Be(payment.PaymentNumber);
        db.ActivityLogs.Should().Contain(a => a.Action == "payment-milestone-paid" && a.EntityType == "SalesOrder");
    }

    [Fact]
    public async Task A_partial_payment_leaves_the_milestone_partially_paid()
    {
        using var db = TestDbContextFactory.Create();
        var (milestone, invoice) = await SeedInvoicedMilestoneAsync(db);

        await PayAsync(db, invoice, 100m);

        milestone.Status.Should().Be(PaymentMilestoneStatus.PartiallyPaid);
        milestone.PaidAmount.Should().Be(100m);
    }

    [Fact]
    public async Task Voiding_the_payment_returns_the_milestone_to_invoiced()
    {
        using var db = TestDbContextFactory.Create();
        var (milestone, invoice) = await SeedInvoicedMilestoneAsync(db);
        var payment = await PayAsync(db, invoice, 300m);

        var settings = new Mock<ISettingsService>();
        var voider = new VoidPaymentHandler(new PaymentRepository(db), db, settings.Object);
        await voider.Handle(new VoidPaymentCommand(payment.Id, new VoidPaymentRequestModel("bounced")), CancellationToken.None);

        milestone.Status.Should().Be(PaymentMilestoneStatus.Invoiced);
        milestone.PaidAmount.Should().BeNull();
        milestone.PaidAt.Should().BeNull();
    }

    [Fact]
    public async Task Mark_paid_refuses_an_invoiced_milestone()
    {
        using var db = TestDbContextFactory.Create();
        var (milestone, _) = await SeedInvoicedMilestoneAsync(db);

        var handler = new MarkMilestonePaidHandler(db, Mock.Of<IClock>());
        var act = () => handler.Handle(new MarkMilestonePaidCommand(milestone.Id, 300m, "CHK-9"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*INV-00077*");
        milestone.PaidAmount.Should().BeNull();
    }
}
