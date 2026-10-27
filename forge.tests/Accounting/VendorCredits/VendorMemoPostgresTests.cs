using FluentAssertions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
using Forge.Api.Features.VendorCredits;
using Forge.Core.Enums;
using Forge.Core.Enums.Accounting;
using Forge.Core.Models;
using Forge.Integrations;
using Forge.Tests.Helpers;

using static Forge.Tests.Accounting.VendorCredits.VendorMemoPgContext;

namespace Forge.Tests.Accounting.VendorCredits;

/// <summary>
/// Vendor credit and debit memos against real Postgres with the built-in ledger on: issue, auto-apply, apply, unapply
/// and void post and move the AP open items so the AP control account always equals the signed open items; a memo
/// line can't credit more of a bill line than was billed, nor above its price; an application across booking rates
/// posts the realized FX difference; aging and the vendor statement net open memos.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class VendorMemoPostgresTests(PostgresFixture fixture)
{
    private readonly VendorMemoPgContext ctx = new(fixture);

    [Fact]
    public async Task Issue_auto_applies_then_unapply_and_void_keep_control_equal_to_open_items()
    {
        await ctx.ResetAsync();
        var vendor = await ctx.VendorAsync("Steel Supply");
        var bill = await ctx.PostedBillAsync(vendor, 10m, 50m, "BILL-1");

        var draft = await ctx.CreateMemoAsync(new CreateVendorCreditMemoRequestModel(
            vendor, VendorCreditMemoSourceType.Return, "Two damaged",
            [new VendorCreditMemoLineRequestModel(2m, 50m, VendorBillLineId: bill.Lines.Single().Id)],
            VendorBillId: bill.Id, VendorReference: "VC-77"));
        draft.Status.Should().Be("Draft");
        draft.MemoNumber.Should().Be("VCM-000001");

        var issued = await ctx.IssueAsync(draft.Id);
        issued.Status.Should().Be("Applied");
        issued.IssueJournalEntryId.Should().NotBeNull();
        (await ctx.BillAsync(bill.Id)).Should().Match<Forge.Core.Entities.VendorBill>(b => b.BalanceDue == 400m && b.Status == VendorBillStatus.PartiallyPaid);

        var net = await ctx.NetByAccountAsync();
        net[ApId].Should().Be(-400m);
        net[ExpenseId].Should().Be(400m);
        (await ctx.SignedApOpenItemsAsync()).Should().Be(400m);

        await using (var db = ctx.Db())
        {
            var unapplied = await new UnapplyVendorCreditMemoHandler(db, MemoPosting(db), new SystemClock(), ctx.Mediator(db).Object)
                .Handle(new UnapplyVendorCreditMemoCommand(issued.Id, issued.Applications.Single().Id), CancellationToken.None);
            unapplied.Status.Should().Be("Open");
            unapplied.OpenAmount.Should().Be(100m);
        }
        (await ctx.BillAsync(bill.Id)).Should().Match<Forge.Core.Entities.VendorBill>(b => b.BalanceDue == 500m && b.Status == VendorBillStatus.Approved);
        (await ctx.SignedApOpenItemsAsync()).Should().Be(400m);
        (await ctx.NetByAccountAsync())[ApId].Should().Be(-400m);

        await using (var db = ctx.Db())
        {
            var voided = await new VoidVendorCreditMemoHandler(db, MemoPosting(db), new SystemClock(), ctx.Mediator(db).Object)
                .Handle(new VoidVendorCreditMemoCommand(issued.Id, "Vendor withdrew it"), CancellationToken.None);
            voided.Status.Should().Be("Voided");
        }
        (await ctx.NetByAccountAsync())[ApId].Should().Be(-500m);
        (await ctx.SignedApOpenItemsAsync()).Should().Be(500m);
    }

    [Fact]
    public async Task A_line_crediting_a_bill_line_is_capped_by_quantity_and_price()
    {
        await ctx.ResetAsync();
        var vendor = await ctx.VendorAsync("Steel Supply");
        var bill = await ctx.PostedBillAsync(vendor, 10m, 50m, "BILL-1");
        var lineId = bill.Lines.Single().Id;

        var tooMany = await ctx.CreateMemoAsync(new CreateVendorCreditMemoRequestModel(
            vendor, VendorCreditMemoSourceType.Return, "Too many", [new VendorCreditMemoLineRequestModel(11m, 50m, lineId)], VendorBillId: bill.Id));
        (await FluentActions.Awaiting(() => ctx.IssueAsync(tooMany.Id)).Should().ThrowAsync<ValidationException>())
            .Which.Errors.Should().Contain(e => e.ErrorCode == "vendor-credit-exceeds-bill-line");

        var overpriced = await ctx.CreateMemoAsync(new CreateVendorCreditMemoRequestModel(
            vendor, VendorCreditMemoSourceType.PriceAdjustment, "Overpriced", [new VendorCreditMemoLineRequestModel(1m, 60m, lineId)], VendorBillId: bill.Id));
        (await FluentActions.Awaiting(() => ctx.IssueAsync(overpriced.Id)).Should().ThrowAsync<ValidationException>())
            .Which.Errors.Should().Contain(e => e.ErrorCode == "vendor-credit-price-exceeds-bill-line");

        var first = await ctx.CreateMemoAsync(new CreateVendorCreditMemoRequestModel(
            vendor, VendorCreditMemoSourceType.Return, "Eight back", [new VendorCreditMemoLineRequestModel(8m, 50m, lineId)], VendorBillId: bill.Id));
        await ctx.IssueAsync(first.Id);
        var second = await ctx.CreateMemoAsync(new CreateVendorCreditMemoRequestModel(
            vendor, VendorCreditMemoSourceType.Return, "Three more", [new VendorCreditMemoLineRequestModel(3m, 50m, lineId)], VendorBillId: bill.Id));
        (await FluentActions.Awaiting(() => ctx.IssueAsync(second.Id)).Should().ThrowAsync<ValidationException>())
            .Which.Errors.Should().Contain(e => e.ErrorCode == "vendor-credit-exceeds-bill-line" && e.ErrorMessage.Contains("at most 2"));
    }

    [Fact]
    public async Task Applying_a_memo_booked_at_another_rate_posts_the_realized_fx_difference()
    {
        await ctx.ResetAsync();
        var vendor = await ctx.VendorAsync("Euro Parts");
        var bill = await ctx.PostedBillAsync(vendor, 1m, 1000m, "BILL-EUR", EurId, 1.10m);
        var memo = await ctx.CreateMemoAsync(new CreateVendorCreditMemoRequestModel(
            vendor, VendorCreditMemoSourceType.Rebate, "Volume rebate", [new VendorCreditMemoLineRequestModel(1m, 100m)],
            CurrencyId: EurId, FxRate: 1.05m));
        await ctx.IssueAsync(memo.Id);

        await using (var db = ctx.Db())
        {
            var applied = await new ApplyVendorCreditMemoHandler(db, MemoPosting(db), new SystemClock(), ctx.Mediator(db).Object)
                .Handle(new ApplyVendorCreditMemoCommand(memo.Id, bill.Id, 100m), CancellationToken.None);
            applied.Status.Should().Be("Applied");
        }

        await using (var db = ctx.Db())
        {
            var fx = await db.JournalEntries.Include(e => e.Lines)
                .SingleAsync(e => e.IdempotencyKey!.StartsWith("AP:VendorCreditMemoApplication:"));
            fx.Lines.Single(l => l.Debit > 0m && l.GlAccountId == ApId).Debit.Should().Be(110m);
            fx.Lines.Single(l => l.Credit > 0m && l.GlAccountId == ApId).Credit.Should().Be(105m);
            fx.Lines.Single(l => l.GlAccountId == FxGainId).Credit.Should().Be(5m);
        }
        var net = await ctx.NetByAccountAsync();
        (await ctx.SignedApOpenItemsAsync()).Should().Be(-net[ApId]);
        (await ctx.BillAsync(bill.Id)).BalanceDue.Should().Be(900m);
    }

    [Fact]
    public async Task Aging_and_the_vendor_statement_net_open_memos_as_negative_items()
    {
        await ctx.ResetAsync();
        var vendor = await ctx.VendorAsync("Steel Supply");
        var bill = await ctx.PostedBillAsync(vendor, 10m, 50m, "BILL-1");
        var memo = await ctx.CreateMemoAsync(new CreateVendorCreditMemoRequestModel(
            vendor, VendorCreditMemoSourceType.Rebate, "Rebate", [new VendorCreditMemoLineRequestModel(1m, 100m)], MemoDate: March));
        await ctx.IssueAsync(memo.Id);

        await using var db = ctx.Db();
        var aging = await new ApAgingService(db, new SystemClock()).GetApAgingAsync(BookId, new DateOnly(2026, 3, 31));
        aging.GrandTotal.Should().Be(400m);
        aging.Vendors.Single().OpenBalance.Should().Be(400m);
        aging.Reconciliation.ControlBalance.Should().Be(aging.Reconciliation.AgingTotal);

        var statement = await new GetVendorStatementHandler(db, new SystemClock()).Handle(new GetVendorStatementQuery(vendor), CancellationToken.None);
        statement.Lines.Should().HaveCount(2);
        statement.Lines.Single(l => l.DocumentType == "VendorBill").OpenAmount.Should().Be(500m);
        statement.Lines.Single(l => l.DocumentType == "VendorCreditMemo").OpenAmount.Should().Be(-100m);
        statement.Totals.Single().OpenBalance.Should().Be(400m);

        var open = await new GetVendorCreditMemosHandler(db).Handle(new GetVendorCreditMemosQuery(vendor, OpenOnly: true, Search: "rebate"), CancellationToken.None);
        open.Single().OpenAmount.Should().Be(100m);
        bill.Id.Should().BePositive();
    }

    [Fact]
    public async Task Revaluation_counts_vendor_discounts_taken_and_open_vendor_memos()
    {
        await ctx.ResetAsync();
        var vendor = await ctx.VendorAsync("Euro Parts");
        var bill = await ctx.PostedBillAsync(vendor, 1m, 1000m, "BILL-EUR", EurId, 1.10m, discountPercent: 2m);

        await using (var db = ctx.Db())
        {
            await ctx.PaymentHandler(db).Handle(new CreateVendorPaymentCommandBuilder(vendor, bill.Id).Build(), CancellationToken.None);
        }

        var memo = await ctx.CreateMemoAsync(new CreateVendorCreditMemoRequestModel(
            vendor, VendorCreditMemoSourceType.Rebate, "Rebate", [new VendorCreditMemoLineRequestModel(1m, 100m)],
            CurrencyId: EurId, FxRate: 1.10m, MemoDate: March));
        await ctx.IssueAsync(memo.Id);

        await using (var db = ctx.Db())
        {
            var result = await new FxRevaluationService(db, Engine(db))
                .RevalueAsync(BookId, EurId, newRate: 1.20m, new DateOnly(2026, 3, 31), ctx.UserId, preview: true);
            result.NetForeignPosition.Should().Be(-400m);
            result.Adjustment.Should().Be(-40m);
        }
    }

    private sealed class CreateVendorPaymentCommandBuilder(int vendorId, int billId)
    {
        public Forge.Api.Features.VendorPayments.CreateVendorPaymentCommand Build()
            => new(vendorId, "Check", 480m, March.AddDays(5), "CHK-1", null,
                [new CreateVendorPaymentApplicationModel(billId, 480m, SettlementFxRate: 1.15m, DiscountAmount: 20m)],
                CurrencyId: EurId, FxRate: 1.15m);
    }
}
