using FluentAssertions;

using Forge.Api.Features.Accounting;
using Forge.Api.Features.CustomerReturns;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums;
using Forge.Core.Enums.Accounting;
using static Forge.Tests.Accounting.CreditMemos.CreditMemoTestContext;

namespace Forge.Tests.Accounting.CreditMemos;

/// <summary>The pure parts of credit memos: totals, issue lines, invoice balance, open-item sign and the cost fallback order.</summary>
public class CreditMemoRulesTests
{
    private static CreditMemo Memo(decimal taxRate, params CreditMemoLine[] lines)
        => new() { CreditMemoNumber = "CM-000009", CustomerId = 3, TaxRate = taxRate, Status = CreditMemoStatus.Issued, Lines = lines.ToList() };

    private static CreditMemoLine Line(CreditMemoLineType type, decimal qty, decimal price, string? taxCode = null)
        => new() { LineType = type, Quantity = qty, UnitPrice = price, TaxCode = taxCode, Description = type.ToString() };

    [Fact]
    public void Issue_lines_split_items_freight_and_tax_and_net_the_restocking_fee()
    {
        var memo = Memo(0.10m,
            Line(CreditMemoLineType.Item, 2m, 50m),
            Line(CreditMemoLineType.RestockingFee, 1m, 10m),
            Line(CreditMemoLineType.Freight, 1m, 15m, InvoiceLine.NonTaxableCode));

        memo.Subtotal.Should().Be(105m);
        memo.TaxAmount.Should().Be(9m);
        memo.Total.Should().Be(114m);

        var lines = CreditMemoPostingService.BuildIssueLines(memo, memo.Total);
        lines.Single(l => l.AccountKey == "SALES_RETURNS").Debit.Should().Be(90m);
        lines.Single(l => l.AccountKey == "FREIGHT_INCOME").Debit.Should().Be(15m);
        lines.Single(l => l.AccountKey == "SALES_TAX_PAYABLE").Debit.Should().Be(9m);
        var ar = lines.Single(l => l.AccountKey == "AR_CONTROL");
        ar.Credit.Should().Be(114m);
        ar.PartyId.Should().Be(3);
        lines.Sum(l => l.Debit).Should().Be(lines.Sum(l => l.Credit));
    }

    [Fact]
    public void Exempt_memo_posts_no_tax_line()
    {
        var memo = Memo(0m, Line(CreditMemoLineType.Item, 1m, 40m));
        CreditMemoPostingService.BuildIssueLines(memo, memo.Total).Should().NotContain(l => l.AccountKey == "SALES_TAX_PAYABLE");
    }

    [Fact]
    public void Write_off_lines_are_never_taxed()
    {
        var memo = Memo(0.10m, Line(CreditMemoLineType.WriteOff, 1m, 75m));
        memo.TaxAmount.Should().Be(0m);
        var lines = CreditMemoPostingService.BuildWriteOffLines(memo, memo.Total, 0m);
        lines.Single(l => l.AccountKey == "BAD_DEBT_EXPENSE").Debit.Should().Be(75m);
        lines.Should().NotContain(l => l.AccountKey == "SALES_TAX_PAYABLE");
    }

    [Fact]
    public void Write_off_tax_share_is_proportional_to_the_invoice_total()
    {
        var invoice = new Invoice
        {
            TaxRate = 0.0725m,
            Lines = [new InvoiceLine { Quantity = 60m, UnitPrice = 189m, LineNumber = 1, Description = "Widget" }],
        };
        CreditMemoPostingService.WriteOffTaxShare(invoice, 12162.15m).Should().Be(822.15m);
        CreditMemoPostingService.WriteOffTaxShare(invoice, 100m).Should().Be(6.76m);
    }

    [Fact]
    public void Invoice_balance_subtracts_live_credits_only()
    {
        var invoice = new Invoice
        {
            Lines = [new InvoiceLine { Quantity = 1m, UnitPrice = 500m, LineNumber = 1, Description = "Widget" }],
            PaymentApplications = [new PaymentApplication { Amount = 100m, DiscountAmount = 10m }],
            CreditApplications =
            [
                new CreditMemoApplication { Amount = 50m },
                new CreditMemoApplication { Amount = 70m, ReversedAt = Today },
            ],
        };
        invoice.CreditsApplied.Should().Be(50m);
        invoice.BalanceDue.Should().Be(340m);
    }

    [Fact]
    public void Credit_memo_open_items_carry_a_negative_open_amount()
    {
        var item = new ArOpenItem { Sign = -1, OriginalTxnAmount = 200m, OriginalFunctionalAmount = 220m, AppliedTxnAmount = 50m, AppliedFunctionalAmount = 55m };
        item.OpenTxnAmount.Should().Be(-150m);
        item.OpenFunctionalAmount.Should().Be(-165m);
        item.RecomputeStatus();
        item.Status.Should().Be(OpenItemStatus.PartiallyApplied);
    }

    [Fact]
    public void Memo_status_follows_what_is_used()
    {
        var memo = Memo(0m, Line(CreditMemoLineType.Item, 1m, 100m));
        memo.Applications.Add(new CreditMemoApplication { Amount = 40m });
        memo.RecomputeStatus();
        memo.Status.Should().Be(CreditMemoStatus.PartiallyApplied);
        memo.Refunds.Add(new CustomerRefund { Amount = 60m });
        memo.RecomputeStatus();
        memo.Status.Should().Be(CreditMemoStatus.Refunded);
        memo.OpenAmount.Should().Be(0m);
    }

    [Fact]
    public async Task Original_cost_falls_back_from_cogs_to_standard_to_the_store_average()
    {
        using var ctx = await CreateAsync();
        var part = await ctx.FinishedGoodAsync(standardCost: 0m, number: "NO-STD");
        part.ManualCostOverride = null;
        ctx.Db.InventoryValuations.Add(new InventoryValuation { BookId = BookId, PartId = part.Id, OnHandQuantity = 4m, TotalValue = 30m, AverageUnitCost = 7.5m });
        await ctx.Db.SaveChangesAsync();
        var ret = new CustomerReturn { ReturnNumber = "RMA-1", Reason = "x" };

        (await ctx.Dispositions().OriginalUnitCostAsync(ret, part, BookId, CancellationToken.None)).Should().Be((7.5m, false));

        part.ManualCostOverride = 12m;
        (await ctx.Dispositions().OriginalUnitCostAsync(ret, part, BookId, CancellationToken.None)).Should().Be((12m, false));
    }
}
