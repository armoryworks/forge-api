using Microsoft.EntityFrameworkCore;

using Forge.Api.Services.Fx;
using Forge.Core.Entities;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// Period-end unrealized FX revaluation. The monetary position of a currency is its foreign cash (journal
/// lines on CASH accounts posted in that currency) plus its open receivables less its open payables (the AR
/// and AP open items, which carry both the transaction and the booked functional amount). Settlement entries
/// post in the functional currency, so the open items, not the control-account lines, are what still stands
/// open in the foreign currency. An invoice or bill counts as open at the as-of date less only the payments
/// applied on or before it, so a close run after a payment that came in the next period still revalues it.
/// A payment voided since has had its applications removed and no longer counts against the document.
/// </summary>
public sealed class FxRevaluationService(AppDbContext db, IPostingEngine postingEngine) : IFxRevaluationService
{
    private const string KeyFxRevaluation = "FX_REVALUATION";
    private const string KeyFxGain = "FX_GAIN";
    private const string KeyFxLoss = "FX_LOSS";
    private const string RevalSourceType = "FxRevaluation";

    public async Task<FxRevaluationResult> RevalueAsync(
        int bookId, int currencyId, decimal? newRate, DateOnly asOf, int postedByUserId,
        CancellationToken ct = default, bool preview = false)
    {
        var book = await db.Books.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bookId, ct)
            ?? throw new PostingException("NO_POSTING_BOOK", $"Book {bookId} not found for FX revaluation.");

        var code = await db.Currencies.AsNoTracking()
            .Where(c => c.Id == currencyId).Select(c => c.Code).FirstOrDefaultAsync(ct);

        if (currencyId == book.FunctionalCurrencyId)
            return new FxRevaluationResult(bookId, currencyId, asOf, 0m, 0m, null, 1m, 0m, code);

        var (netForeign, netFunctional) = await PositionAsync(bookId, currencyId, asOf, ct);
        if (netForeign == 0m && netFunctional == 0m)
            return new FxRevaluationResult(bookId, currencyId, asOf, 0m, 0m, null, newRate ?? 0m, 0m, code);

        var rate = newRate
            ?? (await new DocumentCurrencyResolver(db).GetRateAsync(currencyId, book.FunctionalCurrencyId, asOf, ct)).Rate;

        var reMeasured = Math.Round(netForeign * rate, 2, MidpointRounding.AwayFromZero);
        var adjustment = reMeasured - netFunctional;

        if (adjustment == 0m || preview)
            return new FxRevaluationResult(bookId, currencyId, asOf, netForeign, adjustment, null, rate, netFunctional, code);

        var lines = adjustment > 0m
            ? new List<PostingLine>
              {
                  new() { AccountKey = KeyFxRevaluation, Debit = adjustment, Description = "FX revaluation adjustment" },
                  new() { AccountKey = KeyFxGain, Credit = adjustment, Description = "Unrealized FX gain" },
              }
            : new List<PostingLine>
              {
                  new() { AccountKey = KeyFxLoss, Debit = -adjustment, Description = "Unrealized FX loss" },
                  new() { AccountKey = KeyFxRevaluation, Credit = -adjustment, Description = "FX revaluation adjustment" },
              };

        var entry = await postingEngine.PostAsync(new PostingRequest
        {
            BookId = bookId,
            EntryDate = asOf,
            Source = JournalSource.FX,
            SourceType = RevalSourceType,
            SourceId = currencyId,
            CurrencyId = book.FunctionalCurrencyId,
            Memo = $"Unrealized FX revaluation — {code ?? $"currency {currencyId}"} @ {rate}",
            IdempotencyKey = $"{JournalSource.FX}:Reval:{bookId}:{currencyId}:{asOf:yyyyMMdd}",
            AutoReverseNextPeriod = true,
            Lines = lines,
        }, postedByUserId, ct);

        return new FxRevaluationResult(bookId, currencyId, asOf, netForeign, adjustment, entry.Id, rate, netFunctional, code);
    }

    public async Task<IReadOnlyList<FxRevaluationResult>> RevalueAllAsync(
        int bookId, DateOnly asOf, int postedByUserId, CancellationToken ct = default, bool preview = false)
    {
        var book = await db.Books.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bookId, ct)
            ?? throw new PostingException("NO_POSTING_BOOK", $"Book {bookId} not found for FX revaluation.");

        var currencyIds = await db.Currencies.AsNoTracking()
            .Where(c => c.IsActive && c.Id != book.FunctionalCurrencyId)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Code)
            .Select(c => c.Id)
            .ToListAsync(ct);

        var results = new List<FxRevaluationResult>();
        foreach (var currencyId in currencyIds)
        {
            var (netForeign, netFunctional) = await PositionAsync(bookId, currencyId, asOf, ct);
            if (netForeign == 0m && netFunctional == 0m)
                continue;
            results.Add(await RevalueAsync(bookId, currencyId, null, asOf, postedByUserId, ct, preview));
        }
        return results;
    }

    private async Task<(decimal Foreign, decimal Functional)> PositionAsync(
        int bookId, int currencyId, DateOnly asOf, CancellationToken ct)
    {
        var cashIds = await db.AccountDeterminationRules
            .Where(r => r.BookId == bookId && r.Key == "CASH").Select(r => r.GlAccountId).ToListAsync(ct);

        var cash = await
            (from line in db.JournalLines.IgnoreQueryFilters()
             join je in db.JournalEntries.IgnoreQueryFilters() on line.JournalEntryId equals je.Id
             where je.BookId == bookId
                 && line.CurrencyId == currencyId
                 && cashIds.Contains(line.GlAccountId)
                 && (je.Status == JournalEntryStatus.Posted || je.Status == JournalEntryStatus.Reversed)
                 && je.SourceType != RevalSourceType
                 && je.EntryDate <= asOf
             select new
             {
                 Foreign = line.Debit - line.Credit,
                 Functional = line.Debit > 0 ? line.FunctionalAmount : -line.FunctionalAmount,
             })
            .ToListAsync(ct);

        var asOfEnd = new DateTimeOffset(asOf.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
        var receivableItems = await db.ArOpenItems.AsNoTracking()
            .Where(i => i.BookId == bookId && i.CurrencyId == currencyId && i.DocumentDate <= asOfEnd
                && i.Status != OpenItemStatus.Voided)
            .Select(i => new OpenItemRow(
                i.SourceType, i.SourceId, i.Status, i.FxRate,
                i.OriginalTxnAmount, i.OriginalFunctionalAmount, i.AppliedTxnAmount, i.AppliedFunctionalAmount, i.Sign))
            .ToListAsync(ct);
        var payableItems = await db.ApOpenItems.AsNoTracking()
            .Where(i => i.BookId == bookId && i.CurrencyId == currencyId && i.DocumentDate <= asOfEnd
                && i.Status != OpenItemStatus.Voided)
            .Select(i => new OpenItemRow(
                i.SourceType, i.SourceId, i.Status, i.FxRate,
                i.OriginalTxnAmount, i.OriginalFunctionalAmount, i.AppliedTxnAmount, i.AppliedFunctionalAmount))
            .ToListAsync(ct);

        var receivables = OpenAsOf(receivableItems, "Invoice", await InvoiceApplicationsAsync(bookId, receivableItems, ct), asOf);
        var payables = OpenAsOf(payableItems, "VendorBill", await BillApplicationsAsync(payableItems, ct), asOf);

        return (
            cash.Sum(r => r.Foreign) + receivables.Foreign - payables.Foreign,
            cash.Sum(r => r.Functional) + receivables.Functional - payables.Functional);
    }

    /// <summary>
    /// What an item still had open at the end of <paramref name="asOf"/>: its original amount less the
    /// applications that took effect on or before that day, relieved at the item's booking rate exactly as the
    /// settlement entries relieve the control account. An item of another source type keeps its current
    /// applied amount and counts only while it is still open.
    /// </summary>
    private static (decimal Foreign, decimal Functional) OpenAsOf(
        List<OpenItemRow> items, string documentSourceType,
        List<(int DocumentId, decimal Amount, DateOnly EffectiveDate)> applications, DateOnly asOf)
    {
        var applied = applications
            .Where(a => a.EffectiveDate <= asOf)
            .GroupBy(a => a.DocumentId)
            .ToDictionary(g => g.Key, g => g.Select(a => a.Amount).ToList());

        decimal foreign = 0m, functional = 0m;
        foreach (var item in items)
        {
            if (item.SourceType == documentSourceType)
            {
                var amounts = applied.GetValueOrDefault(item.SourceId) ?? [];
                foreign += item.OriginalTxn - amounts.Sum();
                functional += item.OriginalFunctional
                    - amounts.Sum(a => Math.Round(a * item.FxRate, 2, MidpointRounding.AwayFromZero));
            }
            else if (item.Status is OpenItemStatus.Open or OpenItemStatus.PartiallyApplied)
            {
                foreign += item.Sign * (item.OriginalTxn - item.AppliedTxn);
                functional += item.Sign * (item.OriginalFunctional - item.AppliedFunctional);
            }
        }
        return (foreign, functional);
    }

    private async Task<List<(int DocumentId, decimal Amount, DateOnly EffectiveDate)>> InvoiceApplicationsAsync(
        int bookId, List<OpenItemRow> items, CancellationToken ct)
    {
        var invoiceIds = items.Where(i => i.SourceType == "Invoice").Select(i => i.SourceId).ToList();
        if (invoiceIds.Count == 0)
            return [];

        var applications = await db.Set<PaymentApplication>().AsNoTracking()
            .Where(a => invoiceIds.Contains(a.InvoiceId))
            .Select(a => new { a.Id, a.InvoiceId, Amount = a.Amount + a.DiscountAmount, a.Payment.PaymentDate })
            .ToListAsync(ct);
        var credits = await db.CreditMemoApplications.AsNoTracking()
            .Where(a => invoiceIds.Contains(a.InvoiceId) && a.ReversedAt == null)
            .Select(a => new { a.InvoiceId, a.Amount, a.AppliedAt })
            .ToListAsync(ct);

        var applyKeys = applications
            .Select(a => $"{JournalSource.AR}:PaymentApplication:{a.Id}:APPLY")
            .ToList();
        var applyDates = await db.JournalEntries.IgnoreQueryFilters().AsNoTracking()
            .Where(e => e.BookId == bookId && e.IdempotencyKey != null && applyKeys.Contains(e.IdempotencyKey))
            .Select(e => new { e.IdempotencyKey, e.EntryDate })
            .ToDictionaryAsync(e => e.IdempotencyKey!, e => e.EntryDate, ct);

        return applications
            .Select(a => (
                a.InvoiceId,
                a.Amount,
                applyDates.TryGetValue($"{JournalSource.AR}:PaymentApplication:{a.Id}:APPLY", out var applied)
                    ? applied
                    : DateOnly.FromDateTime(a.PaymentDate.UtcDateTime)))
            .Concat(credits.Select(c => (c.InvoiceId, c.Amount, DateOnly.FromDateTime(c.AppliedAt.UtcDateTime))))
            .ToList();
    }

    private async Task<List<(int DocumentId, decimal Amount, DateOnly EffectiveDate)>> BillApplicationsAsync(
        List<OpenItemRow> items, CancellationToken ct)
    {
        var billIds = items.Where(i => i.SourceType == "VendorBill").Select(i => i.SourceId).ToList();
        if (billIds.Count == 0)
            return [];

        var applications = await db.Set<VendorPaymentApplication>().AsNoTracking()
            .Where(a => billIds.Contains(a.VendorBillId))
            .Select(a => new { a.VendorBillId, a.Amount, a.VendorPayment.PaymentDate })
            .ToListAsync(ct);

        return applications
            .Select(a => (a.VendorBillId, a.Amount, DateOnly.FromDateTime(a.PaymentDate.UtcDateTime)))
            .ToList();
    }

    private sealed record OpenItemRow(
        string SourceType, int SourceId, OpenItemStatus Status, decimal FxRate,
        decimal OriginalTxn, decimal OriginalFunctional, decimal AppliedTxn, decimal AppliedFunctional, short Sign = 1);
}
