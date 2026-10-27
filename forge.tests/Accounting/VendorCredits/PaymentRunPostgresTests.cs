using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.PaymentRuns;
using Forge.Api.Features.VendorPayments;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Tests.Helpers;

using static Forge.Tests.Accounting.VendorCredits.VendorMemoPgContext;

namespace Forge.Tests.Accounting.VendorCredits;

/// <summary>
/// Payment runs against real Postgres: a run is a persisted batch with per-bill status; a failure after the first
/// vendor leaves the run Interrupted with that vendor paid, and resuming pays only the rest; open vendor credits are
/// deducted from each vendor's payment, and cover a vendor's whole run without a payment when they can.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PaymentRunPostgresTests(PostgresFixture fixture)
{
    private readonly VendorMemoPgContext ctx = new(fixture);

    [Fact]
    public async Task A_failure_after_the_first_vendor_interrupts_the_run_and_resume_pays_only_the_rest()
    {
        await ctx.ResetAsync();
        var bills = new List<(int VendorId, int BillId)>();
        foreach (var name in new[] { "Alpha", "Bravo", "Charlie" })
        {
            var vendor = await ctx.VendorAsync(name);
            var bill = await ctx.PostedBillAsync(vendor, 1m, 100m, $"BILL-{name}");
            bills.Add((vendor, bill.Id));
        }

        var failOnce = bills[1].VendorId;
        PaymentRunDetailResponseModel run;
        await using (var db = ctx.Db())
        {
            var mediator = ctx.Mediator(db);
            var real = ctx.PaymentHandler(db);
            var sender = ctx.Mediator(db, command =>
            {
                if (command.VendorId == failOnce)
                {
                    failOnce = 0;
                    throw new InvalidOperationException("Bank connection dropped");
                }
                return real.Handle(command, CancellationToken.None);
            });
            run = await new CreatePaymentRunHandler(db, ctx.Executor(db, sender.Object), mediator.Object).Handle(
                new CreatePaymentRunCommand(new CreatePaymentRunRequestModel(
                    March.AddDays(2), "Check", bills.Select(b => new VendorPaymentSuggestionSelectionModel(b.BillId, 100m)).ToList(), "RUN-1")),
                CancellationToken.None);
        }

        run.Status.Should().Be("Interrupted");
        run.LastError.Should().Contain("Bank connection dropped");
        run.Items.Single(i => i.VendorId == bills[0].VendorId).Status.Should().Be("Paid");
        run.Items.Single(i => i.VendorId == bills[1].VendorId).Status.Should().Be("Failed");
        run.Items.Single(i => i.VendorId == bills[2].VendorId).Status.Should().Be("Pending");
        await using (var db = ctx.Db())
            (await db.VendorPayments.CountAsync()).Should().Be(1);

        await using (var db = ctx.Db())
        {
            var resumed = await new ResumePaymentRunHandler(db, ctx.Executor(db, ctx.Mediator(db).Object), ctx.Mediator(db).Object)
                .Handle(new ResumePaymentRunCommand(run.Id), CancellationToken.None);
            resumed.Status.Should().Be("Completed");
            resumed.Items.Should().OnlyContain(i => i.Status == "Paid" && i.VendorPaymentId != null);
        }

        await using (var verify = ctx.Db())
        {
            var payments = await verify.VendorPayments.ToListAsync();
            payments.Should().HaveCount(3);
            payments.Should().OnlyContain(p => p.PaymentRunId == run.Id);
            payments.GroupBy(p => p.VendorId).Should().OnlyContain(g => g.Count() == 1);
            (await verify.VendorBills.CountAsync(b => b.Status == VendorBillStatus.Paid)).Should().Be(3);
        }
        (await ctx.NetByAccountAsync())[ApId].Should().Be(0m);
    }

    [Fact]
    public async Task Open_vendor_credits_are_deducted_and_can_settle_a_vendor_without_a_payment()
    {
        await ctx.ResetAsync();
        var deducted = await ctx.VendorAsync("Deducted");
        var deductedBill = await ctx.PostedBillAsync(deducted, 1m, 500m, "BILL-D");
        var covered = await ctx.VendorAsync("Covered");
        var coveredBill = await ctx.PostedBillAsync(covered, 1m, 80m, "BILL-C");
        foreach (var vendor in new[] { deducted, covered })
        {
            var memo = await ctx.CreateMemoAsync(new CreateVendorCreditMemoRequestModel(
                vendor, VendorCreditMemoSourceType.Rebate, "Rebate", [new VendorCreditMemoLineRequestModel(1m, 100m)], MemoDate: March));
            await ctx.IssueAsync(memo.Id);
        }

        PaymentRunDetailResponseModel run;
        await using (var db = ctx.Db())
        {
            var mediator = ctx.Mediator(db);
            run = await new CreatePaymentRunHandler(db, ctx.Executor(db, mediator.Object), mediator.Object).Handle(
                new CreatePaymentRunCommand(new CreatePaymentRunRequestModel(
                    March.AddDays(2), "Check",
                    [new VendorPaymentSuggestionSelectionModel(deductedBill.Id, 500m), new VendorPaymentSuggestionSelectionModel(coveredBill.Id, 80m)])),
                CancellationToken.None);
        }

        run.Status.Should().Be("Completed");
        var deductedItem = run.Items.Single(i => i.VendorBillId == deductedBill.Id);
        deductedItem.CreditAmount.Should().Be(100m);
        deductedItem.CashAmount.Should().Be(400m);
        var coveredItem = run.Items.Single(i => i.VendorBillId == coveredBill.Id);
        coveredItem.CreditAmount.Should().Be(80m);
        coveredItem.VendorPaymentId.Should().BeNull();

        await using (var db = ctx.Db())
        {
            var payment = await db.VendorPayments.SingleAsync();
            payment.VendorId.Should().Be(deducted);
            payment.Amount.Should().Be(400m);
            (await db.VendorCreditMemoApplications.SingleAsync(a => a.VendorBillId == deductedBill.Id)).VendorPaymentId.Should().Be(payment.Id);
            (await db.VendorCreditMemoApplications.SingleAsync(a => a.VendorBillId == coveredBill.Id)).PaymentRunId.Should().Be(run.Id);
        }
        (await ctx.BillAsync(deductedBill.Id)).Status.Should().Be(VendorBillStatus.Paid);
        (await ctx.BillAsync(coveredBill.Id)).Status.Should().Be(VendorBillStatus.Paid);
        var net = await ctx.NetByAccountAsync();
        (await ctx.SignedApOpenItemsAsync()).Should().Be(-net[ApId]);
        (await ctx.SignedApOpenItemsAsync()).Should().Be(-20m);
    }

    [Fact]
    public async Task Credits_covering_a_vendor_spread_over_every_pick_so_no_bill_is_paid_in_cash()
    {
        await ctx.ResetAsync();
        var vendor = await ctx.VendorAsync("Spread");
        var first = await ctx.PostedBillAsync(vendor, 1m, 100m, "BILL-S1");
        var second = await ctx.PostedBillAsync(vendor, 1m, 100m, "BILL-S2");
        var memo = await ctx.CreateMemoAsync(new CreateVendorCreditMemoRequestModel(
            vendor, VendorCreditMemoSourceType.Rebate, "Rebate", [new VendorCreditMemoLineRequestModel(1m, 100m)], MemoDate: March));
        await ctx.IssueAsync(memo.Id);

        PaymentRunDetailResponseModel run;
        await using (var db = ctx.Db())
        {
            var mediator = ctx.Mediator(db);
            run = await new CreatePaymentRunHandler(db, ctx.Executor(db, mediator.Object), mediator.Object).Handle(
                new CreatePaymentRunCommand(new CreatePaymentRunRequestModel(
                    March.AddDays(2), "Check",
                    [new VendorPaymentSuggestionSelectionModel(first.Id, 50m), new VendorPaymentSuggestionSelectionModel(second.Id, 50m)],
                    ApplyVendorCredits: true)),
                CancellationToken.None);
        }

        run.Status.Should().Be("Completed");
        run.Items.Should().OnlyContain(i => i.CreditAmount == 50m && i.CashAmount == 0m && i.VendorPaymentId == null);
        await using (var db = ctx.Db())
            (await db.VendorPayments.CountAsync()).Should().Be(0);
        (await ctx.BillAsync(first.Id)).BalanceDue.Should().Be(50m);
        (await ctx.BillAsync(second.Id)).BalanceDue.Should().Be(50m);
        var net = await ctx.NetByAccountAsync();
        (await ctx.SignedApOpenItemsAsync()).Should().Be(-net[ApId]);
        (await ctx.SignedApOpenItemsAsync()).Should().Be(100m);
    }

    [Fact]
    public async Task Voiding_a_payment_that_deducted_a_credit_puts_the_credit_back()
    {
        await ctx.ResetAsync();
        var vendor = await ctx.VendorAsync("Deducted");
        var bill = await ctx.PostedBillAsync(vendor, 1m, 500m, "BILL-D");
        var memo = await ctx.CreateMemoAsync(new CreateVendorCreditMemoRequestModel(
            vendor, VendorCreditMemoSourceType.Rebate, "Rebate", [new VendorCreditMemoLineRequestModel(1m, 100m)], MemoDate: March));
        await ctx.IssueAsync(memo.Id);

        int paymentId;
        await using (var db = ctx.Db())
        {
            var payment = await ctx.PaymentHandler(db).Handle(new CreateVendorPaymentCommand(
                vendor, "Check", 400m, March.AddDays(2), "CHK-9", null,
                [new CreateVendorPaymentApplicationModel(bill.Id, 400m)],
                Credits: [new CreateVendorPaymentCreditModel(memo.Id, bill.Id, 100m)]), CancellationToken.None);
            paymentId = payment.Id;
        }
        (await ctx.BillAsync(bill.Id)).Status.Should().Be(VendorBillStatus.Paid);

        await using (var db = ctx.Db())
        {
            await new VoidVendorPaymentHandler(new Forge.Data.Repositories.VendorPaymentRepository(db), db, new Forge.Integrations.SystemClock(),
                    new Forge.Api.Features.Accounting.VendorPaymentCashPostingService(db, Engine(db), Caps()), ctx.Http(), MemoPosting(db))
                .Handle(new VoidVendorPaymentCommand(paymentId, "Wrong amount"), CancellationToken.None);
        }

        var reopened = await ctx.BillAsync(bill.Id);
        reopened.BalanceDue.Should().Be(500m);
        reopened.Status.Should().Be(VendorBillStatus.Approved);
        await using (var db = ctx.Db())
            (await db.VendorCreditMemos.Include(m => m.Lines).Include(m => m.Applications).SingleAsync()).OpenAmount.Should().Be(100m);
        (await ctx.NetByAccountAsync())[ApId].Should().Be(-400m);
        (await ctx.SignedApOpenItemsAsync()).Should().Be(400m);
    }
}
