using System.Security.Claims;

using FluentAssertions;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.CreditMemos;
using Forge.Api.Features.CustomerReturns;
using Forge.Api.Features.Invoices;
using Forge.Api.Middleware;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums;
using Forge.Core.Enums.Accounting;
using Forge.Core.Models;
using Forge.Core.Settings;
using Forge.Data.Repositories;
using static Forge.Tests.Accounting.CreditMemos.CreditMemoTestContext;

namespace Forge.Tests.Accounting.CreditMemos;

/// <summary>
/// Credit memos, write-offs, refunds and RMA dispositions end to end on the handlers and the real posting engine:
/// what each posts, how invoice balances and open items move, and the guards around voids and over-crediting.
/// </summary>
public class CreditMemoPostingTests
{
    private static IMediator Mediator(CreditMemoTestContext ctx)
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<GetCreditMemoByIdQuery>(), It.IsAny<CancellationToken>()))
            .Returns((GetCreditMemoByIdQuery q, CancellationToken ct) => new GetCreditMemoByIdHandler(ctx.Db).Handle(q, ct));
        return mediator.Object;
    }

    private static async Task<CreditMemoDetailResponseModel> CreateAsync(CreditMemoTestContext ctx, CreateCreditMemoRequestModel data)
        => await new CreateCreditMemoHandler(ctx.Db, ctx.Clock, Mediator(ctx)).Handle(new CreateCreditMemoCommand(data), CancellationToken.None);

    private static async Task<CreditMemoDetailResponseModel> IssueAsync(CreditMemoTestContext ctx, int id)
        => await new IssueCreditMemoHandler(ctx.Db, ctx.Issuer(), Mediator(ctx)).Handle(new IssueCreditMemoCommand(id), CancellationToken.None);

    private static async Task<Invoice> ReloadInvoiceAsync(CreditMemoTestContext ctx, int id)
    {
        ctx.Db.ChangeTracker.Clear();
        return await ctx.Db.Invoices.Include(i => i.Lines).Include(i => i.PaymentApplications).Include(i => i.CreditApplications)
            .SingleAsync(i => i.Id == id);
    }

    private static async Task<(Invoice Invoice, Part Part, CustomerReturn Return)> InvoicedReturnAsync(CreditMemoTestContext ctx)
    {
        var customer = await ctx.CustomerAsync();
        var part = await ctx.FinishedGoodAsync();
        var invoice = await ctx.PostedInvoiceAsync(customer,
            [new InvoiceLine { PartId = part.Id, Description = "Widget", Quantity = 60m, UnitPrice = 189m, LineNumber = 1 }],
            taxRate: 0.0725m);
        var ret = new CustomerReturn
        {
            ReturnNumber = "RMA-00001", CustomerId = customer.Id, Reason = "Wrong colour", ReturnDate = Today,
            Quantity = 10m, InvoiceId = invoice.Id, InvoiceLineId = invoice.Lines.Single().Id,
        };
        ctx.Db.CustomerReturns.Add(ret);
        await ctx.Db.SaveChangesAsync();
        return (invoice, part, ret);
    }

    [Fact]
    public async Task Rma_return_to_stock_credits_the_invoice_and_puts_the_goods_back_at_original_cost()
    {
        using var ctx = await CreditMemoTestContext.CreateAsync();
        var (invoice, part, ret) = await InvoicedReturnAsync(ctx);
        var location = await ctx.LocationAsync();

        (await ctx.NetByAccountAsync())[ArId].Should().Be(12162.15m);

        await ctx.ResolveHandler().Handle(new ResolveCustomerReturnCommand(
            ret.Id, "Checked", ReturnDisposition.ReturnToStock, 10m, location.Id), CancellationToken.None);

        var memo = await ctx.Db.CreditMemos.Include(m => m.Lines).Include(m => m.Applications).SingleAsync();
        memo.Total.Should().Be(2027.03m);
        memo.Status.Should().Be(CreditMemoStatus.Applied);
        memo.CustomerReturnId.Should().Be(ret.Id);

        var issue = await ctx.Db.JournalEntries.Include(e => e.Lines)
            .SingleAsync(e => e.IdempotencyKey == $"AR:CreditMemo:{memo.Id}:ISSUE");
        issue.Lines.Single(l => l.GlAccountId == ReturnsId).Debit.Should().Be(1890.00m);
        issue.Lines.Single(l => l.GlAccountId == TaxId).Debit.Should().Be(137.03m);
        issue.Lines.Single(l => l.GlAccountId == ArId).Credit.Should().Be(2027.03m);

        var restock = await ctx.Db.JournalEntries.Include(e => e.Lines)
            .SingleAsync(e => e.IdempotencyKey == $"Inventory:CustomerReturn:{ret.Id}:RESTOCK");
        restock.Lines.Single(l => l.GlAccountId == InventoryFgId).Debit.Should().Be(1000m);
        restock.Lines.Single(l => l.GlAccountId == CogsId).Credit.Should().Be(1000m);

        var bin = await ctx.Db.BinContents.SingleAsync(b => b.EntityId == part.Id);
        bin.Quantity.Should().Be(10m);
        bin.LocationId.Should().Be(location.Id);
        (await ctx.Db.BinMovements.SingleAsync()).Reason.Should().Be(BinMovementReason.Return);
        (await ctx.Db.InventoryValuations.SingleAsync(v => v.PartId == part.Id)).OnHandQuantity.Should().Be(10m);

        var reloaded = await ReloadInvoiceAsync(ctx, invoice.Id);
        reloaded.CreditsApplied.Should().Be(2027.03m);
        reloaded.BalanceDue.Should().Be(10135.12m);
        reloaded.Status.Should().Be(InvoiceStatus.PartiallyPaid);

        var net = await ctx.NetByAccountAsync();
        net[ArId].Should().Be(10135.12m);
        (await ctx.SignedOpenItemsAsync()).Should().Be(net[ArId]);

        var resolved = await ctx.Db.CustomerReturns.SingleAsync();
        resolved.Status.Should().Be(CustomerReturnStatus.Resolved);
        resolved.Disposition.Should().Be(ReturnDisposition.ReturnToStock);
        resolved.QuantityRestocked.Should().Be(10m);
        resolved.CreditMemoId.Should().Be(memo.Id);
        resolved.RestockBinMovementId.Should().NotBeNull();
    }

    [Fact]
    public async Task Rma_scrap_moves_the_original_cost_from_cogs_to_write_down_and_adds_no_stock()
    {
        using var ctx = await CreditMemoTestContext.CreateAsync();
        var (_, _, ret) = await InvoicedReturnAsync(ctx);

        await ctx.ResolveHandler().Handle(new ResolveCustomerReturnCommand(
            ret.Id, null, ReturnDisposition.Scrap, 4m, IssueCredit: false), CancellationToken.None);

        var scrap = await ctx.Db.JournalEntries.Include(e => e.Lines)
            .SingleAsync(e => e.IdempotencyKey == $"Inventory:CustomerReturn:{ret.Id}:SCRAP");
        scrap.Lines.Single(l => l.GlAccountId == WritedownId).Debit.Should().Be(400m);
        scrap.Lines.Single(l => l.GlAccountId == CogsId).Credit.Should().Be(400m);
        (await ctx.Db.BinContents.CountAsync()).Should().Be(0);
        (await ctx.Db.CreditMemos.CountAsync()).Should().Be(0);
        (await ctx.Db.CustomerReturns.SingleAsync()).QuantityScrapped.Should().Be(4m);
    }

    [Fact]
    public async Task Rma_restock_without_a_posted_cogs_offsets_inventory_write_down_at_standard()
    {
        using var ctx = await CreditMemoTestContext.CreateAsync();
        ctx.FullGl = false;
        var (_, _, ret) = await InvoicedReturnAsync(ctx);
        ctx.FullGl = true;
        var location = await ctx.LocationAsync();

        await ctx.ResolveHandler().Handle(new ResolveCustomerReturnCommand(
            ret.Id, null, ReturnDisposition.ReturnToStock, 2m, location.Id, IssueCredit: false), CancellationToken.None);

        var restock = await ctx.Db.JournalEntries.Include(e => e.Lines)
            .SingleAsync(e => e.IdempotencyKey == $"Inventory:CustomerReturn:{ret.Id}:RESTOCK");
        restock.Lines.Single(l => l.GlAccountId == InventoryFgId).Debit.Should().Be(200m);
        restock.Lines.Single(l => l.GlAccountId == WritedownId).Credit.Should().Be(200m);
    }

    [Fact]
    public async Task Rma_resolve_refuses_more_than_was_returned_and_restock_without_a_location()
    {
        using var ctx = await CreditMemoTestContext.CreateAsync();
        var (_, _, ret) = await InvoicedReturnAsync(ctx);
        var location = await ctx.LocationAsync();

        var tooMany = () => ctx.ResolveHandler().Handle(new ResolveCustomerReturnCommand(
            ret.Id, null, ReturnDisposition.ReturnToStock, 11m, location.Id), CancellationToken.None);
        (await tooMany.Should().ThrowAsync<ValidationException>())
            .Which.Errors.Should().Contain(e => e.ErrorCode == ReturnResolveCodes.QuantityExceedsReturned);

        var validator = new ResolveCustomerReturnValidator();
        var result = await validator.ValidateAsync(new ResolveCustomerReturnCommand(ret.Id, null, ReturnDisposition.ReturnToStock, 1m));
        result.Errors.Should().Contain(e => e.ErrorCode == ReturnResolveCodes.LocationRequired);
    }

    [Fact]
    public async Task Rma_restocking_fee_reduces_the_credit()
    {
        using var ctx = await CreditMemoTestContext.CreateAsync();
        var (_, _, ret) = await InvoicedReturnAsync(ctx);

        await ctx.ResolveHandler().Handle(new ResolveCustomerReturnCommand(
            ret.Id, null, ReturnDisposition.NoReturn, 10m, RestockingFeePercent: 10m), CancellationToken.None);

        var memo = await ctx.Db.CreditMemos.Include(m => m.Lines).SingleAsync();
        memo.Lines.Should().Contain(l => l.LineType == CreditMemoLineType.RestockingFee && l.UnitPrice == 189m);
        memo.Subtotal.Should().Be(1701m);
        memo.Total.Should().Be(1701m + 123.32m);
        (await ctx.Db.JournalEntries.CountAsync(e => e.IdempotencyKey!.StartsWith("Inventory:CustomerReturn"))).Should().Be(0);
    }

    [Fact]
    public async Task Channel_returns_never_issue_a_credit_memo()
    {
        using var ctx = await CreditMemoTestContext.CreateAsync();
        var (_, _, ret) = await InvoicedReturnAsync(ctx);
        var channel = new SalesChannel { Name = "Marketplace", Code = "MKT" };
        ctx.Db.Set<SalesChannel>().Add(channel);
        await ctx.Db.SaveChangesAsync();
        ret.ChannelId = channel.Id;
        await ctx.Db.SaveChangesAsync();

        await ctx.ResolveHandler().Handle(new ResolveCustomerReturnCommand(
            ret.Id, null, ReturnDisposition.NoReturn, 10m), CancellationToken.None);

        (await ctx.Db.CreditMemos.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Write_off_of_the_balance_posts_bad_debt_and_closes_the_invoice()
    {
        using var ctx = await CreditMemoTestContext.CreateAsync();
        var customer = await ctx.CustomerAsync();
        var invoice = await ctx.PostedInvoiceAsync(customer,
            [new InvoiceLine { Description = "Service", Quantity = 1m, UnitPrice = 196m, LineNumber = 1 }]);

        var memo = await new WriteOffInvoiceHandler(ctx.Db, ctx.Issuer(), ctx.Clock, Mediator(ctx))
            .Handle(new WriteOffInvoiceCommand(invoice.Id, null, "Customer out of business"), CancellationToken.None);

        memo.ReasonType.Should().Be("WriteOff");
        memo.Status.Should().Be("Applied");
        var net = await ctx.NetByAccountAsync();
        net[BadDebtId].Should().Be(196m);
        net[ArId].Should().Be(0m);
        net.GetValueOrDefault(TaxId).Should().Be(0m);
        (await ReloadInvoiceAsync(ctx, invoice.Id)).Status.Should().Be(InvoiceStatus.Paid);
        (await ctx.SignedOpenItemsAsync()).Should().Be(0m);
    }

    [Fact]
    public async Task Write_off_moves_the_tax_share_when_the_setting_says_so()
    {
        using var ctx = await CreditMemoTestContext.CreateAsync();
        ctx.Db.SystemSettings.Add(new SystemSetting { Key = CreditMemoSettings.WriteOffReversesTaxKey, Value = "true" });
        await ctx.Db.SaveChangesAsync();
        var customer = await ctx.CustomerAsync();
        var invoice = await ctx.PostedInvoiceAsync(customer,
            [new InvoiceLine { Description = "Widget", Quantity = 1m, UnitPrice = 1000m, LineNumber = 1 }], taxRate: 0.08m);

        await new WriteOffInvoiceHandler(ctx.Db, ctx.Issuer(), ctx.Clock, Mediator(ctx))
            .Handle(new WriteOffInvoiceCommand(invoice.Id, 540m, "Partial write-off"), CancellationToken.None);

        var net = await ctx.NetByAccountAsync();
        net[TaxId].Should().Be(-80m + 40m);
        net[BadDebtId].Should().Be(500m);
        net[ArId].Should().Be(1080m - 540m);
        (await ReloadInvoiceAsync(ctx, invoice.Id)).BalanceDue.Should().Be(540m);
    }

    [Fact]
    public async Task Write_off_above_the_balance_is_refused()
    {
        using var ctx = await CreditMemoTestContext.CreateAsync();
        var customer = await ctx.CustomerAsync();
        var invoice = await ctx.PostedInvoiceAsync(customer,
            [new InvoiceLine { Description = "Service", Quantity = 1m, UnitPrice = 100m, LineNumber = 1 }]);

        var act = () => new WriteOffInvoiceHandler(ctx.Db, ctx.Issuer(), ctx.Clock, Mediator(ctx))
            .Handle(new WriteOffInvoiceCommand(invoice.Id, 100.01m, "Too much"), CancellationToken.None);

        (await act.Should().ThrowAsync<ValidationException>())
            .Which.Errors.Should().Contain(e => e.ErrorCode == CreditMemoRules.WriteOffExceedsBalanceCode);
    }

    [Fact]
    public async Task Credit_memo_endpoints_refuse_a_write_off_reason_or_line()
    {
        using var ctx = await CreditMemoTestContext.CreateAsync();
        var customer = await ctx.CustomerAsync();

        var byReason = () => CreateAsync(ctx, new CreateCreditMemoRequestModel(customer.Id, null, CreditMemoReasonType.WriteOff,
            "Bad debt", null, [new CreditMemoLineRequestModel(CreditMemoLineType.Item, null, null, "Bad debt", 1m, 50m)]));
        var byLine = () => CreateAsync(ctx, new CreateCreditMemoRequestModel(customer.Id, null, CreditMemoReasonType.Goodwill,
            "Goodwill", null, [new CreditMemoLineRequestModel(CreditMemoLineType.WriteOff, null, null, "Bad debt", 1m, 50m)]));

        (await byReason.Should().ThrowAsync<ValidationException>())
            .Which.Errors.Should().Contain(e => e.ErrorCode == CreditMemoRules.WriteOffReservedCode);
        (await byLine.Should().ThrowAsync<ValidationException>())
            .Which.Errors.Should().Contain(e => e.ErrorCode == CreditMemoRules.WriteOffReservedCode);
        (await ctx.Db.CreditMemos.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Write_off_is_never_refunded_and_only_an_admin_or_manager_unapplies_it()
    {
        using var ctx = await CreditMemoTestContext.CreateAsync();
        var customer = await ctx.CustomerAsync();
        var invoice = await ctx.PostedInvoiceAsync(customer,
            [new InvoiceLine { Description = "Service", Quantity = 1m, UnitPrice = 196m, LineNumber = 1 }]);
        var memo = await new WriteOffInvoiceHandler(ctx.Db, ctx.Issuer(), ctx.Clock, Mediator(ctx))
            .Handle(new WriteOffInvoiceCommand(invoice.Id, null, "Customer out of business"), CancellationToken.None);

        var asOfficeManager = () => new UnapplyCreditMemoHandler(ctx.Db, ctx.Posting(), ctx.Clock, Mediator(ctx), UserInRole("OfficeManager"))
            .Handle(new UnapplyCreditMemoCommand(memo.Id, memo.Applications.Single().Id), CancellationToken.None);
        await asOfficeManager.Should().ThrowAsync<ForbiddenException>();

        memo = await new UnapplyCreditMemoHandler(ctx.Db, ctx.Posting(), ctx.Clock, Mediator(ctx), UserInRole("Manager"))
            .Handle(new UnapplyCreditMemoCommand(memo.Id, memo.Applications.Single().Id), CancellationToken.None);
        memo.OpenAmount.Should().Be(196m);

        var reapply = () => new ApplyCreditMemoHandler(ctx.Db, ctx.Posting(), ctx.Clock, Mediator(ctx), UserInRole("OfficeManager"))
            .Handle(new ApplyCreditMemoCommand(memo.Id, invoice.Id, 196m), CancellationToken.None);
        await reapply.Should().ThrowAsync<ForbiddenException>();

        var refund = () => new RefundCreditMemoHandler(ctx.Db, ctx.Posting(), Mediator(ctx), new CreditMemoRefundSync(ctx.Db))
            .Handle(new RefundCreditMemoCommand(memo.Id, new CustomerRefundRequestModel(196m, PaymentMethod.Check, Today, null)),
                CancellationToken.None);
        (await refund.Should().ThrowAsync<ValidationException>())
            .Which.Errors.Should().Contain(e => e.ErrorCode == CreditMemoRules.WriteOffRefundCode);
        (await ctx.Db.CustomerRefunds.CountAsync()).Should().Be(0);
    }

    private static IHttpContextAccessor UserInRole(string role)
        => new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], "Test")),
            },
        };

    [Fact]
    public async Task Crediting_more_than_was_invoiced_on_a_line_is_refused_across_memos()
    {
        using var ctx = await CreditMemoTestContext.CreateAsync();
        var customer = await ctx.CustomerAsync();
        var invoice = await ctx.PostedInvoiceAsync(customer,
            [new InvoiceLine { Description = "Widget", Quantity = 5m, UnitPrice = 10m, LineNumber = 1 }]);
        var lineId = invoice.Lines.Single().Id;

        var first = await CreateAsync(ctx, new CreateCreditMemoRequestModel(customer.Id, invoice.Id, CreditMemoReasonType.Return,
            "Damaged", null, [new CreditMemoLineRequestModel(CreditMemoLineType.Item, null, lineId, null, 3m, 10m)]));
        await IssueAsync(ctx, first.Id);

        var second = await CreateAsync(ctx, new CreateCreditMemoRequestModel(customer.Id, invoice.Id, CreditMemoReasonType.Return,
            "Damaged again", null, [new CreditMemoLineRequestModel(CreditMemoLineType.Item, null, lineId, null, 3m, 10m)]));
        var act = () => IssueAsync(ctx, second.Id);

        (await act.Should().ThrowAsync<ValidationException>())
            .Which.Errors.Should().Contain(e => e.ErrorCode == CreditMemoRules.CreditExceedsInvoiceLineCode);
    }

    [Fact]
    public async Task Standalone_credit_is_kept_as_customer_credit_then_refunded()
    {
        using var ctx = await CreditMemoTestContext.CreateAsync();
        var customer = await ctx.CustomerAsync();

        var memo = await CreateAsync(ctx, new CreateCreditMemoRequestModel(customer.Id, null, CreditMemoReasonType.Goodwill,
            "Late delivery", null, [new CreditMemoLineRequestModel(CreditMemoLineType.Item, null, null, "Goodwill credit", 1m, 500m)]));
        memo = await IssueAsync(ctx, memo.Id);
        memo.Status.Should().Be("Issued");
        memo.OpenAmount.Should().Be(500m);
        (await ctx.SignedOpenItemsAsync()).Should().Be(-500m);
        (await ctx.NetByAccountAsync())[ArId].Should().Be(-500m);

        memo = await new RefundCreditMemoHandler(ctx.Db, ctx.Posting(), Mediator(ctx), new CreditMemoRefundSync(ctx.Db))
            .Handle(new RefundCreditMemoCommand(memo.Id, new CustomerRefundRequestModel(500m, PaymentMethod.Check, Today, "CHK 9001")),
                CancellationToken.None);

        memo.Status.Should().Be("Refunded");
        memo.OpenAmount.Should().Be(0m);
        var net = await ctx.NetByAccountAsync();
        net[ArId].Should().Be(0m);
        net[CashId].Should().Be(-500m);
        net[ReturnsId].Should().Be(500m);
        (await ctx.SignedOpenItemsAsync()).Should().Be(0m);
        (await ctx.Db.CustomerRefunds.SingleAsync()).RefundNumber.Should().Be("RF-000001");
    }

    [Fact]
    public async Task Refund_above_the_open_credit_is_refused()
    {
        using var ctx = await CreditMemoTestContext.CreateAsync();
        var customer = await ctx.CustomerAsync();
        var memo = await CreateAsync(ctx, new CreateCreditMemoRequestModel(customer.Id, null, CreditMemoReasonType.Other,
            "Overbilled", null, [new CreditMemoLineRequestModel(CreditMemoLineType.Item, null, null, "Adjustment", 1m, 50m)]));
        await IssueAsync(ctx, memo.Id);

        var act = () => new RefundCreditMemoHandler(ctx.Db, ctx.Posting(), Mediator(ctx), new CreditMemoRefundSync(ctx.Db))
            .Handle(new RefundCreditMemoCommand(memo.Id, new CustomerRefundRequestModel(60m, PaymentMethod.Check, Today, null)),
                CancellationToken.None);

        (await act.Should().ThrowAsync<ValidationException>())
            .Which.Errors.Should().Contain(e => e.ErrorCode == CreditMemoRules.RefundExceedsOpenCode);
    }

    [Fact]
    public async Task Apply_unapply_and_void_restore_everything()
    {
        using var ctx = await CreditMemoTestContext.CreateAsync();
        var customer = await ctx.CustomerAsync();
        var invoice = await ctx.PostedInvoiceAsync(customer,
            [new InvoiceLine { Description = "Widget", Quantity = 1m, UnitPrice = 300m, LineNumber = 1 }]);
        var memo = await CreateAsync(ctx, new CreateCreditMemoRequestModel(customer.Id, null, CreditMemoReasonType.PriceAdjustment,
            "Price match", null, [new CreditMemoLineRequestModel(CreditMemoLineType.Item, null, null, "Price match", 1m, 120m)]));
        await IssueAsync(ctx, memo.Id);

        memo = await new ApplyCreditMemoHandler(ctx.Db, ctx.Posting(), ctx.Clock, Mediator(ctx))
            .Handle(new ApplyCreditMemoCommand(memo.Id, invoice.Id, 120m), CancellationToken.None);
        memo.Status.Should().Be("Applied");
        (await ReloadInvoiceAsync(ctx, invoice.Id)).BalanceDue.Should().Be(180m);

        var voidWhileApplied = () => new VoidCreditMemoHandler(ctx.Db, ctx.Posting(), ctx.Clock, Mediator(ctx))
            .Handle(new VoidCreditMemoCommand(memo.Id, "Mistake"), CancellationToken.None);
        (await voidWhileApplied.Should().ThrowAsync<ValidationException>())
            .Which.Errors.Should().Contain(e => e.ErrorCode == CreditMemoRules.CreditMemoAppliedCode);

        var invoiceVoid = () => new VoidInvoiceHandler(new InvoiceRepository(ctx.Db), ctx.InvoicePosting(), null, ctx.Db, ctx.Clock)
            .Handle(new VoidInvoiceCommand(invoice.Id), CancellationToken.None);
        (await invoiceVoid.Should().ThrowAsync<ValidationException>())
            .Which.Errors.Should().Contain(e => e.ErrorCode == CreditMemoRules.InvoiceHasCreditsCode);

        memo = await new UnapplyCreditMemoHandler(ctx.Db, ctx.Posting(), ctx.Clock, Mediator(ctx))
            .Handle(new UnapplyCreditMemoCommand(memo.Id, memo.Applications.Single().Id), CancellationToken.None);
        memo.Status.Should().Be("Issued");
        var reloaded = await ReloadInvoiceAsync(ctx, invoice.Id);
        reloaded.BalanceDue.Should().Be(300m);
        reloaded.Status.Should().Be(InvoiceStatus.Sent);

        memo = await new VoidCreditMemoHandler(ctx.Db, ctx.Posting(), ctx.Clock, Mediator(ctx))
            .Handle(new VoidCreditMemoCommand(memo.Id, "Entered in error"), CancellationToken.None);
        memo.Status.Should().Be("Voided");
        memo.OpenAmount.Should().Be(0m);

        var net = await ctx.NetByAccountAsync();
        net[ArId].Should().Be(300m);
        net[ReturnsId].Should().Be(0m);
        (await ctx.SignedOpenItemsAsync()).Should().Be(300m);
        (await ctx.Db.ArOpenItems.SingleAsync(i => i.SourceType == "CreditMemo")).Status.Should().Be(OpenItemStatus.Voided);
    }

    [Fact]
    public async Task Applying_a_credit_at_another_rate_posts_the_fx_difference()
    {
        using var ctx = await CreditMemoTestContext.CreateAsync();
        var customer = await ctx.CustomerAsync();
        var invoice = await ctx.PostedInvoiceAsync(customer,
            [new InvoiceLine { Description = "Widget", Quantity = 1m, UnitPrice = 1000m, LineNumber = 1 }],
            currencyId: EurId, fxRate: 1.10m);
        var memo = await CreateAsync(ctx, new CreateCreditMemoRequestModel(customer.Id, null, CreditMemoReasonType.PriceAdjustment,
            "Rebate", null, [new CreditMemoLineRequestModel(CreditMemoLineType.Item, null, null, "Rebate", 1m, 100m)],
            CurrencyId: EurId, FxRate: 1.20m));
        await IssueAsync(ctx, memo.Id);

        await new ApplyCreditMemoHandler(ctx.Db, ctx.Posting(), ctx.Clock, Mediator(ctx))
            .Handle(new ApplyCreditMemoCommand(memo.Id, invoice.Id, 100m), CancellationToken.None);

        var fx = await ctx.Db.JournalEntries.Include(e => e.Lines)
            .SingleAsync(e => e.IdempotencyKey!.StartsWith("AR:CreditMemoApplication:"));
        fx.Lines.Single(l => l.GlAccountId == ArId && l.Debit > 0m).Debit.Should().Be(120m);
        fx.Lines.Single(l => l.GlAccountId == ArId && l.Credit > 0m).Credit.Should().Be(110m);
        fx.Lines.Single(l => l.GlAccountId == FxGainId).Credit.Should().Be(10m);

        var net = await ctx.NetByAccountAsync();
        (await ctx.SignedOpenItemsAsync()).Should().Be(net[ArId]);
    }

    [Fact]
    public async Task Same_rate_application_posts_nothing_but_moves_both_open_items()
    {
        using var ctx = await CreditMemoTestContext.CreateAsync();
        var customer = await ctx.CustomerAsync();
        var invoice = await ctx.PostedInvoiceAsync(customer,
            [new InvoiceLine { Description = "Widget", Quantity = 1m, UnitPrice = 400m, LineNumber = 1 }]);
        var memo = await CreateAsync(ctx, new CreateCreditMemoRequestModel(customer.Id, invoice.Id, CreditMemoReasonType.PriceAdjustment,
            "Allowance", null, [new CreditMemoLineRequestModel(CreditMemoLineType.Item, null, null, "Allowance", 1m, 50m)]));

        memo = await IssueAsync(ctx, memo.Id);

        memo.Applications.Should().ContainSingle(a => a.Amount == 50m);
        (await ctx.Db.JournalEntries.CountAsync(e => e.IdempotencyKey!.StartsWith("AR:CreditMemoApplication:"))).Should().Be(0);
        var items = await ctx.Db.ArOpenItems.ToListAsync();
        items.Single(i => i.SourceType == "Invoice").AppliedTxnAmount.Should().Be(50m);
        items.Single(i => i.SourceType == "CreditMemo").Status.Should().Be(OpenItemStatus.Closed);
        (await ctx.SignedOpenItemsAsync()).Should().Be(350m);
    }

    [Fact]
    public async Task Ledger_stays_dark_while_fullgl_is_off_but_balances_move()
    {
        using var ctx = await CreditMemoTestContext.CreateAsync();
        ctx.FullGl = false;
        var customer = await ctx.CustomerAsync();
        var invoice = new Invoice
        {
            InvoiceNumber = "INV-2001", CustomerId = customer.Id, CurrencyId = UsdId, Status = InvoiceStatus.Sent,
            InvoiceDate = Today, DueDate = Today.AddDays(30),
            Lines = [new InvoiceLine { Description = "Widget", Quantity = 2m, UnitPrice = 50m, LineNumber = 1 }],
        };
        ctx.Db.Invoices.Add(invoice);
        await ctx.Db.SaveChangesAsync();

        await new WriteOffInvoiceHandler(ctx.Db, ctx.Issuer(), ctx.Clock, Mediator(ctx))
            .Handle(new WriteOffInvoiceCommand(invoice.Id, null, "Uncollectable"), CancellationToken.None);

        (await ctx.Db.JournalEntries.CountAsync()).Should().Be(0);
        (await ReloadInvoiceAsync(ctx, invoice.Id)).Status.Should().Be(InvoiceStatus.Paid);
    }

    [Fact]
    public async Task Draft_memo_from_an_invoice_takes_its_currency_rate_and_tax()
    {
        using var ctx = await CreditMemoTestContext.CreateAsync();
        var customer = await ctx.CustomerAsync();
        var invoice = await ctx.PostedInvoiceAsync(customer,
            [new InvoiceLine { Description = "Widget", Quantity = 1m, UnitPrice = 100m, LineNumber = 1, TaxCode = InvoiceLine.NonTaxableCode }],
            taxRate: 0.05m, currencyId: EurId, fxRate: 1.25m);

        var memo = await CreateAsync(ctx, new CreateCreditMemoRequestModel(customer.Id, invoice.Id, CreditMemoReasonType.Return,
            "Return", null, [new CreditMemoLineRequestModel(CreditMemoLineType.Item, null, invoice.Lines.Single().Id, null, 1m, 100m)]));

        memo.CurrencyId.Should().Be(EurId);
        memo.FxRate.Should().Be(1.25m);
        memo.TaxRate.Should().Be(0.05m);
        memo.CreditMemoNumber.Should().Be("CM-000001");
        memo.Lines.Single().Taxable.Should().BeFalse();
        memo.Lines.Single().Description.Should().Be("Widget");
        memo.TaxAmount.Should().Be(0m);
    }

    [Fact]
    public async Task Only_drafts_can_be_edited_or_deleted()
    {
        using var ctx = await CreditMemoTestContext.CreateAsync();
        var customer = await ctx.CustomerAsync();
        var memo = await CreateAsync(ctx, new CreateCreditMemoRequestModel(customer.Id, null, CreditMemoReasonType.Other,
            "Adjustment", null, [new CreditMemoLineRequestModel(CreditMemoLineType.Item, null, null, "Adjustment", 1m, 10m)]));

        memo = await new UpdateCreditMemoHandler(ctx.Db, Mediator(ctx)).Handle(new UpdateCreditMemoCommand(memo.Id,
            new UpdateCreditMemoRequestModel(CreditMemoReasonType.Other, "Adjustment", Today,
                [new CreditMemoLineRequestModel(CreditMemoLineType.Item, null, null, "Adjustment", 2m, 10m)])), CancellationToken.None);
        memo.Total.Should().Be(20m);
        memo.Lines.Should().ContainSingle();

        await IssueAsync(ctx, memo.Id);
        var delete = () => new DeleteCreditMemoHandler(ctx.Db, ctx.Clock).Handle(new DeleteCreditMemoCommand(memo.Id), CancellationToken.None);
        (await delete.Should().ThrowAsync<ValidationException>())
            .Which.Errors.Should().Contain(e => e.ErrorCode == CreditMemoRules.CreditMemoNotDraftCode);
    }

    [Fact]
    public async Task Refund_of_unapplied_cash_debits_customer_deposits()
    {
        using var ctx = await CreditMemoTestContext.CreateAsync();
        var customer = await ctx.CustomerAsync();
        var payment = new Payment
        {
            PaymentNumber = "PMT-1", CustomerId = customer.Id, Method = PaymentMethod.Check, Amount = 250m, PaymentDate = Today,
        };
        ctx.Db.Payments.Add(payment);
        await ctx.Db.SaveChangesAsync();
        await new Forge.Api.Features.Accounting.PaymentCashPostingService(ctx.Db, ctx.Engine(), ctx.Caps())
            .PostPaymentCreatedAsync(payment.Id, 7);

        var refund = await new RefundPaymentHandler(ctx.Db, ctx.Posting(), new CreditMemoRefundSync(ctx.Db))
            .Handle(new RefundPaymentCommand(payment.Id, new CustomerRefundRequestModel(100m, PaymentMethod.Check, Today, null)),
                CancellationToken.None);

        refund.PaymentId.Should().Be(payment.Id);
        var net = await ctx.NetByAccountAsync();
        net[DepositsId].Should().Be(-150m);
        net[CashId].Should().Be(150m);
        ctx.Db.ChangeTracker.Clear();
        (await ctx.Db.Payments.Include(p => p.Applications).Include(p => p.Refunds).SingleAsync()).UnappliedAmount.Should().Be(150m);

        var tooMuch = () => new RefundPaymentHandler(ctx.Db, ctx.Posting(), new CreditMemoRefundSync(ctx.Db))
            .Handle(new RefundPaymentCommand(payment.Id, new CustomerRefundRequestModel(151m, PaymentMethod.Check, Today, null)),
                CancellationToken.None);
        (await tooMuch.Should().ThrowAsync<ValidationException>())
            .Which.Errors.Should().Contain(e => e.ErrorCode == CreditMemoRules.RefundExceedsOpenCode);
    }

    [Fact]
    public async Task Voiding_a_refund_reopens_the_credit()
    {
        using var ctx = await CreditMemoTestContext.CreateAsync();
        var customer = await ctx.CustomerAsync();
        var memo = await CreateAsync(ctx, new CreateCreditMemoRequestModel(customer.Id, null, CreditMemoReasonType.Goodwill,
            "Goodwill", null, [new CreditMemoLineRequestModel(CreditMemoLineType.Item, null, null, "Goodwill", 1m, 80m)]));
        await IssueAsync(ctx, memo.Id);
        memo = await new RefundCreditMemoHandler(ctx.Db, ctx.Posting(), Mediator(ctx), new CreditMemoRefundSync(ctx.Db))
            .Handle(new RefundCreditMemoCommand(memo.Id, new CustomerRefundRequestModel(80m, PaymentMethod.Check, Today, null)),
                CancellationToken.None);

        await new VoidCustomerRefundHandler(ctx.Db, ctx.Posting(), ctx.Clock)
            .Handle(new VoidCustomerRefundCommand(memo.Refunds.Single().Id, "Cheque lost"), CancellationToken.None);

        ctx.Db.ChangeTracker.Clear();
        var reloaded = await CreditMemoRules.LoadMemoAsync(ctx.Db, memo.Id, CancellationToken.None);
        reloaded!.Status.Should().Be(CreditMemoStatus.Issued);
        reloaded.OpenAmount.Should().Be(80m);
        var net = await ctx.NetByAccountAsync();
        net[CashId].Should().Be(0m);
        net[ArId].Should().Be(-80m);
        (await ctx.SignedOpenItemsAsync()).Should().Be(-80m);
    }

    [Fact]
    public async Task Tax_liability_and_aging_report_net_the_credit()
    {
        using var ctx = await CreditMemoTestContext.CreateAsync();
        var (_, _, ret) = await InvoicedReturnAsync(ctx);
        await ctx.ResolveHandler().Handle(new ResolveCustomerReturnCommand(
            ret.Id, null, ReturnDisposition.NoReturn, 10m), CancellationToken.None);

        var ledger = await new Forge.Api.Features.Accounting.GetSalesTaxLiabilityHandler(ctx.Db, ctx.Caps())
            .Handle(new Forge.Api.Features.Accounting.GetSalesTaxLiabilityQuery(), CancellationToken.None);
        ledger.TotalLiability.Should().Be(822.15m - 137.03m);
        ledger.TotalTaxableBase.Should().Be(11340m - 1890m);

        ctx.FullGl = false;
        var document = await new Forge.Api.Features.Accounting.GetSalesTaxLiabilityHandler(ctx.Db, ctx.Caps())
            .Handle(new Forge.Api.Features.Accounting.GetSalesTaxLiabilityQuery(), CancellationToken.None);
        document.TotalLiability.Should().Be(822.15m - 137.03m);

        var standalone = await CreateAsync(ctx, new CreateCreditMemoRequestModel(ret.CustomerId, null, CreditMemoReasonType.Goodwill,
            "Goodwill", null, [new CreditMemoLineRequestModel(CreditMemoLineType.Item, null, null, "Goodwill", 1m, 25m)]));
        await IssueAsync(ctx, standalone.Id);
        var aging = await new ReportRepository(ctx.Db).GetArAgingAsync(CancellationToken.None);
        aging.Should().Contain(r => r.DocumentType == "CreditMemo" && r.BalanceDue == -25m);
        aging.Sum(r => r.BalanceDue).Should().Be(10135.12m - 25m);
    }
}
