using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Data.Repositories;

/// <summary>
/// Postgres implementation of <see cref="IAcctNumberSequenceAllocator"/> using a
/// single atomic row-locked <c>UPDATE … RETURNING</c> against
/// <c>acct_number_sequences</c> (the safe <c>JobRepository</c> pattern, NOT
/// <c>InvoiceRepository</c>'s read-max+1) — §5.1. The counter row is created on
/// first use via <c>INSERT … ON CONFLICT DO UPDATE</c>, so the increment and the
/// create-if-missing are one statement that takes the row lock and returns the
/// allocated value. Runs in the caller's transaction so it rolls back with a
/// failed post.
/// </summary>
public sealed class AcctNumberSequenceAllocator(AppDbContext db) : IAcctNumberSequenceAllocator
{
    public async Task<long> AllocateNextAsync(int bookId, int fiscalYearId, CancellationToken ct = default)
    {
        // ON CONFLICT path takes a row lock on the existing counter and bumps it;
        // the INSERT path seeds NextValue=2 and returns the just-consumed 1.
        // RETURNING (next_value - 1) yields the number this caller owns.
        const string sql = @"
INSERT INTO acct_number_sequences (book_id, fiscal_year_id, next_value)
VALUES ({0}, {1}, 2)
ON CONFLICT (book_id, fiscal_year_id)
DO UPDATE SET next_value = acct_number_sequences.next_value + 1
RETURNING next_value - 1 AS ""Value"";";

        var allocated = await db.Database
            .SqlQueryRaw<long>(sql, bookId, fiscalYearId)
            .ToListAsync(ct);

        return allocated[0];
    }
}
