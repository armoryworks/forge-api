using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <inheritdoc />
public sealed class FxRevaluationService(AppDbContext db, IPostingEngine postingEngine) : IFxRevaluationService
{
    private const string KeyFxRevaluation = "FX_REVALUATION";
    private const string KeyFxGain = "FX_GAIN";
    private const string KeyFxLoss = "FX_LOSS";
    private const string RevalSourceType = "FxRevaluation";

    public async Task<FxRevaluationResult> RevalueAsync(
        int bookId, int currencyId, decimal newRate, DateOnly asOf, int postedByUserId, CancellationToken ct = default)
    {
        var book = await db.Books.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bookId, ct)
            ?? throw new PostingException("NO_POSTING_BOOK", $"Book {bookId} not found for FX revaluation.");

        // The functional currency never revalues against itself.
        if (currencyId == book.FunctionalCurrencyId)
            return new FxRevaluationResult(bookId, currencyId, asOf, 0m, 0m, null);

        // Monetary accounts only: cash (CASH key) + AR/AP control. Non-monetary items (revenue, inventory)
        // stay at historical rate.
        var cashIds = await db.AccountDeterminationRules
            .Where(r => r.BookId == bookId && r.Key == "CASH").Select(r => r.GlAccountId).ToListAsync(ct);
        var controlIds = await db.GlAccounts
            .Where(a => a.BookId == bookId && a.IsControlAccount
                && (a.ControlType == ControlAccountType.AR || a.ControlType == ControlAccountType.AP))
            .Select(a => a.Id).ToListAsync(ct);
        var monetary = cashIds.Concat(controlIds).ToHashSet();
        if (monetary.Count == 0)
            return new FxRevaluationResult(bookId, currencyId, asOf, 0m, 0m, null);

        // Net foreign monetary position (transaction + functional), excluding prior reval entries (they
        // auto-reverse, so the historical carrying value is the pre-reval functional).
        var rows = await
            (from line in db.JournalLines.IgnoreQueryFilters()
             join je in db.JournalEntries.IgnoreQueryFilters() on line.JournalEntryId equals je.Id
             where je.BookId == bookId
                 && line.CurrencyId == currencyId
                 && monetary.Contains(line.GlAccountId)
                 && (je.Status == JournalEntryStatus.Posted || je.Status == JournalEntryStatus.Reversed)
                 && je.SourceType != RevalSourceType
                 && je.EntryDate <= asOf
             select new
             {
                 Foreign = line.Debit - line.Credit,
                 Functional = line.Debit > 0 ? line.FunctionalAmount : -line.FunctionalAmount,
             })
            .ToListAsync(ct);

        var netForeign = rows.Sum(r => r.Foreign);
        var netFunctional = rows.Sum(r => r.Functional);
        var reMeasured = Math.Round(netForeign * newRate, 2, MidpointRounding.AwayFromZero);
        var adjustment = reMeasured - netFunctional;

        if (adjustment == 0m)
            return new FxRevaluationResult(bookId, currencyId, asOf, netForeign, 0m, null);

        // Functional-currency reval entry; auto-reverses next period (unrealized).
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
            Memo = $"Unrealized FX revaluation — currency {currencyId} @ {newRate}",
            IdempotencyKey = $"{JournalSource.FX}:Reval:{bookId}:{currencyId}:{asOf:yyyyMMdd}",
            AutoReverseNextPeriod = true,
            Lines = lines,
        }, postedByUserId, ct);

        return new FxRevaluationResult(bookId, currencyId, asOf, netForeign, adjustment, entry.Id);
    }
}
