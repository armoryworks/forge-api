using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities.Accounting;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <inheritdoc />
public sealed class YearEndCloseService(AppDbContext db, IPostingEngine postingEngine, IClock clock)
    : IYearEndCloseService
{
    private const string KeyRetainedEarnings = "RETAINED_EARNINGS";

    public async Task<YearEndCloseResult> CloseYearAsync(
        int fiscalYearId, int closedByUserId, CancellationToken ct = default)
    {
        var year = await db.Set<FiscalYear>()
            .FirstOrDefaultAsync(y => y.Id == fiscalYearId, ct)
            ?? throw new KeyNotFoundException($"Fiscal year {fiscalYearId} not found.");

        if (year.Status == FiscalYearStatus.Closed)
            throw new InvalidOperationException($"Fiscal year {year.Name} is already closed.");

        var book = await db.Books.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == year.BookId, ct)
            ?? throw new PostingException("NO_POSTING_BOOK", $"Book {year.BookId} not found for year-end close.");

        var reAccountId = await db.AccountDeterminationRules
            .Where(r => r.BookId == year.BookId && r.Key == KeyRetainedEarnings)
            .Select(r => (int?)r.GlAccountId)
            .FirstOrDefaultAsync(ct)
            ?? throw new PostingException(
                "NO_RETAINED_EARNINGS",
                "Year-end close needs a RETAINED_EARNINGS account-determination rule, but none is seeded.");

        // Per-P&L-account net debit (Σ Debit − Credit) over the year, excluding any prior closing entry
        // (idempotency would block a re-post anyway, but this keeps the math clean). Aggregate in memory so
        // the signing is provider-agnostic (mirrors FinancialStatementService).
        var rawLines = await
            (from line in db.JournalLines.IgnoreQueryFilters()
             join entry in db.JournalEntries.IgnoreQueryFilters() on line.JournalEntryId equals entry.Id
             join account in db.GlAccounts.IgnoreQueryFilters() on line.GlAccountId equals account.Id
             where entry.BookId == year.BookId
                 && (account.AccountType == AccountType.Income || account.AccountType == AccountType.Expense)
                 && (entry.Status == JournalEntryStatus.Posted || entry.Status == JournalEntryStatus.Reversed)
                 && entry.SourceType != "YearEndClose"
                 && entry.EntryDate >= year.StartDate && entry.EntryDate <= year.EndDate
             select new { line.GlAccountId, Net = line.Debit - line.Credit })
            .ToListAsync(ct);

        var balances = rawLines
            .GroupBy(l => l.GlAccountId)
            .Select(g => (AccountId: g.Key, NetDebit: g.Sum(x => x.Net)))
            .Where(x => x.NetDebit != 0m)
            .OrderBy(x => x.AccountId)
            .ToList();

        // totalNetDebit = expenses − revenues = −net income. The RE offset balances the entry.
        var totalNetDebit = balances.Sum(b => b.NetDebit);
        var netIncome = -totalNetDebit;

        long? entryId = null;
        if (balances.Count > 0)
        {
            var lines = new List<PostingLine>(balances.Count + 1);
            foreach (var (accountId, netDebit) in balances)
            {
                // Zero the account: post the opposite of its balance.
                lines.Add(netDebit > 0m
                    ? new PostingLine { GlAccountId = accountId, Credit = netDebit, Description = "Year-end close" }
                    : new PostingLine { GlAccountId = accountId, Debit = -netDebit, Description = "Year-end close" });
            }

            if (totalNetDebit > 0m)
                lines.Add(new PostingLine { GlAccountId = reAccountId, Debit = totalNetDebit, Description = "Retained earnings (net loss)" });
            else if (totalNetDebit < 0m)
                lines.Add(new PostingLine { GlAccountId = reAccountId, Credit = -totalNetDebit, Description = "Retained earnings (net income)" });
            // totalNetDebit == 0 with non-zero accounts (revenue == expense): the P&L lines self-balance, no RE line.

            var entry = await postingEngine.PostAsync(new PostingRequest
            {
                BookId = year.BookId,
                EntryDate = year.EndDate,
                Source = JournalSource.System,
                SourceType = "YearEndClose",
                SourceId = year.Id,
                CurrencyId = book.FunctionalCurrencyId,
                Memo = $"Year-end close {year.Name}",
                IdempotencyKey = $"{JournalSource.System}:YearEndClose:{year.Id}",
                AllowSoftClosedOverride = true, // the final period may be soft-closed at year-end
                Lines = lines,
            }, closedByUserId, ct);
            entryId = entry.Id;
        }

        // Lock the year: hard-close every period, mark the year Closed. (Posting happened first, while the
        // final period was still postable.)
        var now = clock.UtcNow;
        var periods = await db.FiscalPeriods.Where(p => p.FiscalYearId == year.Id).ToListAsync(ct);
        foreach (var period in periods)
        {
            period.Status = FiscalPeriodStatus.HardClosed;
            period.ClosedByUserId = closedByUserId;
            period.ClosedAt = now;
        }
        year.Status = FiscalYearStatus.Closed;
        year.ClosedByUserId = closedByUserId;
        year.ClosedAt = now;
        await db.SaveChangesAsync(ct);

        return new YearEndCloseResult(year.Id, entryId, netIncome, reAccountId, periods.Count);
    }
}
