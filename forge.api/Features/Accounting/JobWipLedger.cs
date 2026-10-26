using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// A job's GL work-in-process balance (debit-positive), read from the INVENTORY_WIP lines carrying the job's Job
/// dimension: material in + conversion absorbed − finished goods relieved.
/// </summary>
public static class JobWipLedger
{
    public static async Task<decimal> BalanceAsync(
        AppDbContext db, int bookId, int wipAccountId, int jobId, CancellationToken ct)
    {
        var balance = await
            (from line in db.JournalLines.IgnoreQueryFilters()
             join je in db.JournalEntries.IgnoreQueryFilters() on line.JournalEntryId equals je.Id
             where je.BookId == bookId
                 && line.GlAccountId == wipAccountId
                 && line.JobId == jobId
                 && (je.Status == JournalEntryStatus.Posted || je.Status == JournalEntryStatus.Reversed)
             select line.Debit - line.Credit)
            .SumAsync(ct);

        return Math.Round(balance, 2);
    }
}
