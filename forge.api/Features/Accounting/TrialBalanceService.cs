using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// Phase-0 trial balance (§5.3). <b>Filter-immune</b>: uses
/// <c>IgnoreQueryFilters</c> so a soft-deleted row can never silently drop and
/// still "balance" (ledger entities opt out of the global filter anyway, but
/// the query asserts it). Sums <b>functional</b> amounts of <c>Posted</c>
/// entries only (Draft excluded; a Reversed original is itself Posted and its
/// reversal is Posted+equal-and-opposite, so the two net to zero).
/// <para>
/// Phase 0 sums raw <c>JournalLine</c>s for provable correctness; the
/// incremental <see cref="Forge.Core.Entities.Accounting.LedgerBalance"/> is the
/// scale read path wired in Phase 1 (§5.3).
/// </para>
/// </summary>
public sealed class TrialBalanceService(AppDbContext db) : ITrialBalanceService
{
    public async Task<TrialBalance> GetTrialBalanceAsync(
        int bookId,
        DateOnly? fromDate = null,
        DateOnly? toDate = null,
        CancellationToken ct = default)
    {
        // Posted entries for the book in the date window. We include Reversed
        // originals too — wait: a Reversed original's status is Reversed, not
        // Posted, so it would drop, and its (Posted) reversal would remain,
        // double-counting. Instead include BOTH Posted and Reversed headers:
        // the original (now Reversed) + its reversal (Posted) net to zero, which
        // is the correct trial-balance behaviour ("nets Reversed").
        var lines =
            from line in db.JournalLines.IgnoreQueryFilters()
            join entry in db.JournalEntries.IgnoreQueryFilters()
                on line.JournalEntryId equals entry.Id
            where entry.BookId == bookId
                && (entry.Status == JournalEntryStatus.Posted
                    || entry.Status == JournalEntryStatus.Reversed)
                && (fromDate == null || entry.EntryDate >= fromDate)
                && (toDate == null || entry.EntryDate <= toDate)
            join account in db.GlAccounts.IgnoreQueryFilters()
                on line.GlAccountId equals account.Id
            group new { line.FunctionalAmount, line.Debit, line.Credit }
                by new { account.Id, account.AccountNumber, account.Name } into g
            select new TrialBalanceRow
            {
                GlAccountId = g.Key.Id,
                AccountNumber = g.Key.AccountNumber,
                AccountName = g.Key.Name,
                DebitTotal = g.Sum(x => x.Debit > 0 ? x.FunctionalAmount : 0m),
                CreditTotal = g.Sum(x => x.Credit > 0 ? x.FunctionalAmount : 0m),
            };

        var rows = await lines
            .OrderBy(r => r.AccountNumber)
            .ToListAsync(ct);

        return new TrialBalance
        {
            BookId = bookId,
            FromDate = fromDate,
            ToDate = toDate,
            Rows = rows,
            TotalDebit = rows.Sum(r => r.DebitTotal),
            TotalCredit = rows.Sum(r => r.CreditTotal),
        };
    }
}
