using System.Globalization;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Api.Features.Invoices;
using Forge.Api.Services.Fx;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Core.Settings;
using Forge.Data.Context;
using Serilog;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// AR postings for credit memos, their applications to invoices and customer refunds, inline in the operational
/// command's transaction. Every method is a no-op while CAP-ACCT-FULLGL is off; the operational balances
/// (memo open amount, invoice balance due) move regardless.
/// <list type="bullet">
///   <item><b>Issue</b> (<c>AR:CreditMemo:{id}:ISSUE</c>, memo currency at the memo rate): Dr SALES_RETURNS for item
///         lines net of restocking fees, Dr FREIGHT_INCOME for freight lines, Dr SALES_TAX_PAYABLE for the tax, Cr
///         AR_CONTROL for the total. A write-off posts Dr BAD_DEBT_EXPENSE / Cr AR_CONTROL, with the tax share on Dr
///         SALES_TAX_PAYABLE when <c>ar.writeoff_reverses_tax</c> is on. Creates the memo's AR open item with sign -1.</item>
///   <item><b>Application</b>: moves both open items by the amount. No GL when the memo and invoice rates match;
///         otherwise <c>AR:CreditMemoApplication:{id}:FX</c> Dr AR (memo side) / Cr AR (invoice side) with the
///         difference to FX_GAIN or FX_LOSS.</item>
///   <item><b>Refund</b> (<c>AR:CustomerRefund:{id}:REFUND</c>, functional): of a memo, Dr AR_CONTROL at the memo
///         rate; of unapplied cash, Dr CUSTOMER_DEPOSITS at the payment rate; Cr CASH at the refund rate; realized FX
///         plug.</item>
///   <item><b>Void</b>: reverses the issue entry in its own period (the void date when that period is hard-closed)
///         and voids the open item.</item>
/// </list>
/// </summary>
public interface ICreditMemoPostingService
{
    /// <summary>Posts an issued credit memo and creates its AR open item. Idempotent.</summary>
    Task PostIssueAsync(int creditMemoId, int issuedByUserId, CancellationToken ct = default);

    /// <summary>Reverses the issue entry of a credit memo being voided and voids its open item.</summary>
    Task ReverseIssueAsync(int creditMemoId, DateOnly voidDate, int voidedByUserId, CancellationToken ct = default);

    /// <summary>Moves the memo's and the invoice's open items for an application and posts any FX difference.</summary>
    Task PostApplicationAsync(int applicationId, int appliedByUserId, CancellationToken ct = default);

    /// <summary>Undoes <see cref="PostApplicationAsync"/> for an application being unapplied.</summary>
    Task ReverseApplicationAsync(int applicationId, DateOnly reversalDate, int reversedByUserId, CancellationToken ct = default);

    /// <summary>Posts a recorded refund of a credit memo or of unapplied cash. Idempotent.</summary>
    Task PostRefundAsync(int refundId, int recordedByUserId, CancellationToken ct = default);

    /// <summary>Reverses a refund being voided and reopens the memo's open item by its amount.</summary>
    Task ReverseRefundAsync(int refundId, DateOnly reversalDate, int voidedByUserId, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class CreditMemoPostingService(
    AppDbContext db,
    IPostingEngine postingEngine,
    ICapabilitySnapshotProvider capabilities) : ICreditMemoPostingService
{
    private const string FullGlCapability = "CAP-ACCT-FULLGL";

    private const string KeyArControl = "AR_CONTROL";
    private const string KeySalesReturns = "SALES_RETURNS";
    private const string KeyFreightIncome = "FREIGHT_INCOME";
    private const string KeySalesTaxPayable = "SALES_TAX_PAYABLE";
    private const string KeyBadDebt = "BAD_DEBT_EXPENSE";
    private const string KeyCash = "CASH";
    private const string KeyCustomerDeposits = "CUSTOMER_DEPOSITS";
    private const string KeyFxGain = "FX_GAIN";
    private const string KeyFxLoss = "FX_LOSS";

    internal const string OpenItemSourceType = "CreditMemo";

    internal static string IssueKey(int creditMemoId) => $"{JournalSource.AR}:CreditMemo:{creditMemoId}:ISSUE";

    internal static string ApplicationKey(int applicationId) => $"{JournalSource.AR}:CreditMemoApplication:{applicationId}:FX";

    internal static string RefundKey(int refundId) => $"{JournalSource.AR}:CustomerRefund:{refundId}:REFUND";

    public async Task PostIssueAsync(int creditMemoId, int issuedByUserId, CancellationToken ct = default)
    {
        if (!capabilities.IsEnabled(FullGlCapability))
            return;

        var memo = await db.CreditMemos
            .Include(m => m.Lines)
            .FirstOrDefaultAsync(m => m.Id == creditMemoId, ct);
        if (memo is null)
        {
            Log.Warning("Credit memo posting skipped: credit memo {CreditMemoId} not found (FULLGL on).", creditMemoId);
            return;
        }

        var book = await RequireBookAsync("post the credit memo into", ct);
        var key = IssueKey(memo.Id);
        if (await db.JournalEntries.IgnoreQueryFilters().AnyAsync(e => e.BookId == book.Id && e.IdempotencyKey == key, ct))
            return;

        var total = memo.Total;
        if (total <= 0m)
            return;

        var againstDeposit = memo.InvoiceId is int creditedInvoiceId
            && await CustomerDeposits.IsDepositInvoiceAsync(db, creditedInvoiceId, ct);
        var lines = againstDeposit
            ? BuildDepositCreditLines(memo, total)
            : memo.IsWriteOff
                ? await BuildWriteOffLinesAsync(memo, total, ct)
                : BuildIssueLines(memo, total);

        if (!await db.ArOpenItems.IgnoreQueryFilters()
                .AnyAsync(i => i.SourceType == OpenItemSourceType && i.SourceId == memo.Id, ct))
        {
            db.ArOpenItems.Add(new ArOpenItem
            {
                BookId = book.Id,
                CustomerId = memo.CustomerId,
                SourceType = OpenItemSourceType,
                SourceId = memo.Id,
                DocumentNumber = memo.CreditMemoNumber,
                DocumentDate = memo.CreditDate,
                DueDate = memo.CreditDate,
                CurrencyId = memo.CurrencyId,
                FxRate = memo.FxRate,
                OriginalTxnAmount = total,
                OriginalFunctionalAmount = Money.Round(total * memo.FxRate),
                Sign = -1,
                Status = OpenItemStatus.Open,
            });
        }

        await postingEngine.PostAsSystemAsync(new PostingRequest
        {
            BookId = book.Id,
            EntryDate = DateOnly.FromDateTime(memo.CreditDate.UtcDateTime),
            Source = JournalSource.AR,
            SourceType = "CreditMemo",
            SourceId = memo.Id,
            CurrencyId = memo.CurrencyId,
            FxRate = memo.FxRate,
            Memo = memo.IsWriteOff
                ? $"Bad-debt write-off — credit memo {memo.CreditMemoNumber}"
                : $"Credit memo {memo.CreditMemoNumber}",
            IdempotencyKey = key,
            Lines = lines,
        }, issuedByUserId, ct);
    }

    /// <summary>
    /// The issue lines of a non-write-off memo: per line type, rounded line amounts summed (restocking fees reduce the
    /// item credit), the tax, and the AR credit for the total.
    /// </summary>
    internal static List<PostingLine> BuildIssueLines(CreditMemo memo, decimal total)
    {
        var number = memo.CreditMemoNumber;
        var items = memo.Lines
            .Where(l => l.LineType is CreditMemoLineType.Item or CreditMemoLineType.RestockingFee)
            .Sum(l => l.SignedTotal);
        var freight = memo.Lines.Where(l => l.LineType == CreditMemoLineType.Freight).Sum(l => l.SignedTotal);
        var tax = total - Money.Round(memo.Subtotal);

        var lines = new List<PostingLine>(4);
        if (items > 0m)
            lines.Add(new PostingLine { AccountKey = KeySalesReturns, Debit = items, Description = $"Sales returns and allowances — credit memo {number}" });
        if (freight > 0m)
            lines.Add(new PostingLine { AccountKey = KeyFreightIncome, Debit = freight, Description = $"Freight credited — credit memo {number}" });
        if (tax > 0m)
            lines.Add(new PostingLine { AccountKey = KeySalesTaxPayable, Debit = tax, Description = $"Sales tax credited — credit memo {number}" });
        lines.Add(new PostingLine
        {
            AccountKey = KeyArControl,
            PartyType = SubledgerPartyType.Customer,
            PartyId = memo.CustomerId,
            Credit = total,
            Description = $"AR credit — credit memo {number}",
        });
        return lines;
    }

    /// <summary>
    /// The issue lines of a memo (a write-off included) against a payment-milestone invoice: the invoice billed a
    /// deposit, not a sale, so crediting it reduces Customer Deposits rather than booking a return or a bad debt.
    /// </summary>
    internal static List<PostingLine> BuildDepositCreditLines(CreditMemo memo, decimal total)
    {
        var number = memo.CreditMemoNumber;
        return
        [
            new PostingLine { AccountKey = KeyCustomerDeposits, Debit = total, Description = $"Customer deposit credited — credit memo {number}" },
            new PostingLine
            {
                AccountKey = KeyArControl,
                PartyType = SubledgerPartyType.Customer,
                PartyId = memo.CustomerId,
                Credit = total,
                Description = $"AR credit — credit memo {number}",
            },
        ];
    }

    private async Task<List<PostingLine>> BuildWriteOffLinesAsync(CreditMemo memo, decimal total, CancellationToken ct)
    {
        var taxShare = 0m;
        if (memo.InvoiceId is int invoiceId && await WriteOffReversesTaxAsync(ct))
        {
            var invoice = await db.Invoices.AsNoTracking()
                .Include(i => i.Lines)
                .FirstOrDefaultAsync(i => i.Id == invoiceId, ct);
            if (invoice is not null)
                taxShare = WriteOffTaxShare(invoice, total);
        }

        return BuildWriteOffLines(memo, total, taxShare);
    }

    /// <summary>The part of a written-off amount that is the invoice's tax, in proportion to the invoice total.</summary>
    internal static decimal WriteOffTaxShare(Invoice invoice, decimal writtenOff)
    {
        var posted = InvoiceArPostingService.ComputePostedAmounts(invoice);
        return posted.ArTotal <= 0m || posted.TaxAmount <= 0m
            ? 0m
            : Math.Min(posted.TaxAmount, Money.Round(writtenOff * posted.TaxAmount / posted.ArTotal));
    }

    internal static List<PostingLine> BuildWriteOffLines(CreditMemo memo, decimal total, decimal taxShare)
    {
        var number = memo.CreditMemoNumber;
        var lines = new List<PostingLine>(3)
        {
            new() { AccountKey = KeyBadDebt, Debit = total - taxShare, Description = $"Bad debt written off — credit memo {number}" },
        };
        if (taxShare > 0m)
            lines.Add(new PostingLine { AccountKey = KeySalesTaxPayable, Debit = taxShare, Description = $"Sales tax on written-off balance — credit memo {number}" });
        lines.Add(new PostingLine
        {
            AccountKey = KeyArControl,
            PartyType = SubledgerPartyType.Customer,
            PartyId = memo.CustomerId,
            Credit = total,
            Description = $"AR written off — credit memo {number}",
        });
        return lines;
    }

    private async Task<bool> WriteOffReversesTaxAsync(CancellationToken ct)
    {
        var value = await db.SystemSettings.AsNoTracking()
            .Where(s => s.Key == CreditMemoSettings.WriteOffReversesTaxKey)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);
        return bool.TryParse(value, out var on) && on;
    }

    public async Task ReverseIssueAsync(int creditMemoId, DateOnly voidDate, int voidedByUserId, CancellationToken ct = default)
    {
        if (!capabilities.IsEnabled(FullGlCapability))
            return;

        var book = await FindBookAsync(ct);
        if (book is null)
            return;

        var openItem = await MemoOpenItemAsync(creditMemoId, ct);
        if (openItem is not null)
            openItem.Status = OpenItemStatus.Voided;

        var key = IssueKey(creditMemoId);
        var entry = await db.JournalEntries
            .FirstOrDefaultAsync(e => e.BookId == book.Id && e.IdempotencyKey == key && e.Status == JournalEntryStatus.Posted, ct);
        if (entry is null)
        {
            await db.SaveChangesAsync(ct);
            return;
        }

        var reversalDate = await IsHardClosedAsync(book.Id, entry.EntryDate, ct) ? voidDate : entry.EntryDate;
        await postingEngine.ReverseAsSystemAsync(entry.Id, reversalDate, $"Credit memo {creditMemoId} voided", voidedByUserId, ct);
    }

    public async Task PostApplicationAsync(int applicationId, int appliedByUserId, CancellationToken ct = default)
    {
        if (!capabilities.IsEnabled(FullGlCapability))
            return;

        var book = await FindBookAsync(ct);
        if (book is null)
            return;

        var app = await db.CreditMemoApplications
            .Include(a => a.CreditMemo)
            .Include(a => a.Invoice)
            .FirstOrDefaultAsync(a => a.Id == applicationId, ct);
        if (app is null || !await IssuePostedAsync(book.Id, app.CreditMemoId, ct))
            return;

        var key = ApplicationKey(app.Id);
        if (await db.JournalEntries.IgnoreQueryFilters().AnyAsync(e => e.BookId == book.Id && e.IdempotencyKey == key, ct))
            return;

        var memoSide = Money.Round(app.Amount * app.CreditMemo.FxRate);
        var invoiceSide = Money.Round(app.Amount * app.Invoice.FxRate);

        await MoveOpenItemAsync(OpenItemSourceType, app.CreditMemoId, app.Amount, memoSide, ct);
        await MoveOpenItemAsync("Invoice", app.InvoiceId, app.Amount, invoiceSide, ct);

        if (memoSide == invoiceSide)
        {
            await db.SaveChangesAsync(ct);
            return;
        }

        var memoNumber = app.CreditMemo.CreditMemoNumber;
        var invoiceNumber = app.Invoice.InvoiceNumber;
        var lines = new List<PostingLine>(3)
        {
            new()
            {
                AccountKey = KeyArControl,
                PartyType = SubledgerPartyType.Customer,
                PartyId = app.CreditMemo.CustomerId,
                Debit = memoSide,
                Description = $"Credit memo {memoNumber} applied to {invoiceNumber}",
            },
            new()
            {
                AccountKey = KeyArControl,
                PartyType = SubledgerPartyType.Customer,
                PartyId = app.Invoice.CustomerId,
                Credit = invoiceSide,
                Description = $"Invoice {invoiceNumber} settled by credit memo {memoNumber}",
            },
        };
        AddFxPlug(lines, memoSide - invoiceSide, $"credit memo {memoNumber}");

        await postingEngine.PostAsSystemAsync(new PostingRequest
        {
            BookId = book.Id,
            EntryDate = DateOnly.FromDateTime(app.AppliedAt.UtcDateTime),
            Source = JournalSource.AR,
            SourceType = "CreditMemoApplication",
            SourceId = app.Id,
            CurrencyId = book.FunctionalCurrencyId,
            Memo = $"Apply credit memo {memoNumber} to invoice {invoiceNumber}",
            IdempotencyKey = key,
            Lines = lines,
        }, appliedByUserId, ct);
    }

    public async Task ReverseApplicationAsync(int applicationId, DateOnly reversalDate, int reversedByUserId, CancellationToken ct = default)
    {
        if (!capabilities.IsEnabled(FullGlCapability))
            return;

        var book = await FindBookAsync(ct);
        if (book is null)
            return;

        var app = await db.CreditMemoApplications
            .Include(a => a.CreditMemo)
            .Include(a => a.Invoice)
            .FirstOrDefaultAsync(a => a.Id == applicationId, ct);
        if (app is null || !await IssuePostedAsync(book.Id, app.CreditMemoId, ct))
            return;

        await MoveOpenItemAsync(OpenItemSourceType, app.CreditMemoId, -app.Amount, -Money.Round(app.Amount * app.CreditMemo.FxRate), ct);
        await MoveOpenItemAsync("Invoice", app.InvoiceId, -app.Amount, -Money.Round(app.Amount * app.Invoice.FxRate), ct);

        var key = ApplicationKey(app.Id);
        var entry = await db.JournalEntries
            .FirstOrDefaultAsync(e => e.BookId == book.Id && e.IdempotencyKey == key && e.Status == JournalEntryStatus.Posted, ct);
        if (entry is null)
        {
            await db.SaveChangesAsync(ct);
            return;
        }

        await postingEngine.ReverseAsSystemAsync(
            entry.Id, reversalDate, $"Credit memo {app.CreditMemo.CreditMemoNumber} unapplied from {app.Invoice.InvoiceNumber}",
            reversedByUserId, ct);
    }

    public async Task PostRefundAsync(int refundId, int recordedByUserId, CancellationToken ct = default)
    {
        if (!capabilities.IsEnabled(FullGlCapability))
            return;

        var book = await FindBookAsync(ct);
        if (book is null)
            return;

        var refund = await db.CustomerRefunds
            .Include(r => r.CreditMemo)
            .Include(r => r.Payment)
            .FirstOrDefaultAsync(r => r.Id == refundId, ct);
        if (refund is null)
            return;

        var key = RefundKey(refund.Id);
        if (await db.JournalEntries.IgnoreQueryFilters().AnyAsync(e => e.BookId == book.Id && e.IdempotencyKey == key, ct))
            return;

        PostingLine relief;
        if (refund.CreditMemo is { } memo)
        {
            if (!await IssuePostedAsync(book.Id, memo.Id, ct))
                return;

            var memoSide = Money.Round(refund.Amount * memo.FxRate);
            await MoveOpenItemAsync(OpenItemSourceType, memo.Id, refund.Amount, memoSide, ct);
            relief = new PostingLine
            {
                AccountKey = KeyArControl,
                PartyType = SubledgerPartyType.Customer,
                PartyId = refund.CustomerId,
                Debit = memoSide,
                Description = $"Credit memo {memo.CreditMemoNumber} refunded — refund {refund.RefundNumber}",
            };
        }
        else if (refund.Payment is { } payment)
        {
            var originationKey = $"{JournalSource.AR}:Payment:{payment.Id}:PAYMENT";
            if (!await db.JournalEntries.IgnoreQueryFilters().AnyAsync(
                    e => e.BookId == book.Id && e.IdempotencyKey == originationKey
                         && e.Status == JournalEntryStatus.Posted && e.ReversedByEntryId == null, ct))
                return;

            relief = new PostingLine
            {
                AccountKey = KeyCustomerDeposits,
                Debit = FxMath.ToFunctional(refund.Amount, payment.FxRate),
                Description = $"Unapplied cash refunded — payment {payment.PaymentNumber}, refund {refund.RefundNumber}",
            };
        }
        else
        {
            return;
        }

        var cash = FxMath.ToFunctional(refund.Amount, refund.FxRate);
        var lines = new List<PostingLine>(3)
        {
            relief,
            new() { AccountKey = KeyCash, Credit = cash, Description = $"Customer refund {refund.RefundNumber}" },
        };
        AddFxPlug(lines, relief.Debit - cash, $"refund {refund.RefundNumber}");

        await postingEngine.PostAsSystemAsync(new PostingRequest
        {
            BookId = book.Id,
            EntryDate = DateOnly.FromDateTime(refund.RefundDate.UtcDateTime),
            Source = JournalSource.AR,
            SourceType = "CustomerRefund",
            SourceId = refund.Id,
            CurrencyId = book.FunctionalCurrencyId,
            Memo = $"Customer refund {refund.RefundNumber}",
            IdempotencyKey = key,
            Lines = lines,
        }, recordedByUserId, ct);
    }

    public async Task ReverseRefundAsync(int refundId, DateOnly reversalDate, int voidedByUserId, CancellationToken ct = default)
    {
        if (!capabilities.IsEnabled(FullGlCapability))
            return;

        var book = await FindBookAsync(ct);
        if (book is null)
            return;

        var key = RefundKey(refundId);
        var entry = await db.JournalEntries
            .FirstOrDefaultAsync(e => e.BookId == book.Id && e.IdempotencyKey == key && e.Status == JournalEntryStatus.Posted, ct);
        if (entry is null)
            return;

        var refund = await db.CustomerRefunds
            .Include(r => r.CreditMemo)
            .FirstOrDefaultAsync(r => r.Id == refundId, ct);
        if (refund?.CreditMemo is { } memo)
            await MoveOpenItemAsync(OpenItemSourceType, memo.Id, -refund.Amount, -Money.Round(refund.Amount * memo.FxRate), ct);

        await postingEngine.ReverseAsSystemAsync(
            entry.Id, reversalDate, $"Refund {refund?.RefundNumber ?? refundId.ToString(CultureInfo.InvariantCulture)} voided",
            voidedByUserId, ct);
    }

    private static void AddFxPlug(List<PostingLine> lines, decimal debitsOverCredits, string what)
    {
        if (debitsOverCredits > 0m)
            lines.Add(new PostingLine { AccountKey = KeyFxGain, Credit = debitsOverCredits, Description = $"Realized FX gain — {what}" });
        else if (debitsOverCredits < 0m)
            lines.Add(new PostingLine { AccountKey = KeyFxLoss, Debit = -debitsOverCredits, Description = $"Realized FX loss — {what}" });
    }

    private async Task MoveOpenItemAsync(string sourceType, int sourceId, decimal txn, decimal functional, CancellationToken ct)
    {
        var item = await db.ArOpenItems.IgnoreQueryFilters()
            .FirstOrDefaultAsync(i => i.SourceType == sourceType && i.SourceId == sourceId, ct);
        if (item is null)
        {
            Log.Warning("AR open item missing for {SourceType} {SourceId}; credit memo settlement left it untouched.", sourceType, sourceId);
            return;
        }

        item.AppliedTxnAmount = Math.Max(0m, item.AppliedTxnAmount + txn);
        item.AppliedFunctionalAmount = Math.Max(0m, item.AppliedFunctionalAmount + functional);
        item.RecomputeStatus();
    }

    private Task<ArOpenItem?> MemoOpenItemAsync(int creditMemoId, CancellationToken ct)
        => db.ArOpenItems.IgnoreQueryFilters()
            .FirstOrDefaultAsync(i => i.SourceType == OpenItemSourceType && i.SourceId == creditMemoId, ct);

    private async Task<bool> IssuePostedAsync(int bookId, int creditMemoId, CancellationToken ct)
    {
        var key = IssueKey(creditMemoId);
        return await db.JournalEntries.IgnoreQueryFilters()
            .AnyAsync(e => e.BookId == bookId && e.IdempotencyKey == key && e.Status == JournalEntryStatus.Posted, ct);
    }

    private Task<Book?> FindBookAsync(CancellationToken ct)
        => db.Books.AsNoTracking().Where(b => b.IsActive).OrderBy(b => b.Id).FirstOrDefaultAsync(ct);

    private async Task<Book> RequireBookAsync(string purpose, CancellationToken ct)
        => await FindBookAsync(ct)
           ?? throw new PostingException(
               "NO_POSTING_BOOK",
               $"CAP-ACCT-FULLGL is enabled but no active accounting Book is seeded to {purpose}.");

    private async Task<bool> IsHardClosedAsync(int bookId, DateOnly date, CancellationToken ct)
        => await db.FiscalPeriods.AsNoTracking()
            .AnyAsync(p => p.FiscalYear.BookId == bookId
                && p.StartDate <= date && p.EndDate >= date
                && p.Status == FiscalPeriodStatus.HardClosed, ct);
}
