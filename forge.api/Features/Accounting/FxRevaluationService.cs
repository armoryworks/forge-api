using Microsoft.EntityFrameworkCore;

using Forge.Api.Services.Fx;
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
/// open in the foreign currency.
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
        var receivables = await db.ArOpenItems.AsNoTracking()
            .Where(i => i.BookId == bookId && i.CurrencyId == currencyId && i.DocumentDate <= asOfEnd
                && (i.Status == OpenItemStatus.Open || i.Status == OpenItemStatus.PartiallyApplied))
            .Select(i => new
            {
                Foreign = i.OriginalTxnAmount - i.AppliedTxnAmount,
                Functional = i.OriginalFunctionalAmount - i.AppliedFunctionalAmount,
            })
            .ToListAsync(ct);
        var payables = await db.ApOpenItems.AsNoTracking()
            .Where(i => i.BookId == bookId && i.CurrencyId == currencyId && i.DocumentDate <= asOfEnd
                && (i.Status == OpenItemStatus.Open || i.Status == OpenItemStatus.PartiallyApplied))
            .Select(i => new
            {
                Foreign = i.OriginalTxnAmount - i.AppliedTxnAmount,
                Functional = i.OriginalFunctionalAmount - i.AppliedFunctionalAmount,
            })
            .ToListAsync(ct);

        return (
            cash.Sum(r => r.Foreign) + receivables.Sum(r => r.Foreign) - payables.Sum(r => r.Foreign),
            cash.Sum(r => r.Functional) + receivables.Sum(r => r.Functional) - payables.Sum(r => r.Functional));
    }
}
