using Forge.Core.Models.Accounting;

namespace Forge.Core.Interfaces;

/// <summary>
/// Phase-0 reporting read seam (§5.3). Produces a <b>filter-immune</b> trial
/// balance — it ignores the global soft-delete query filter (ledger entities
/// opt out anyway, but the query asserts it via <c>IgnoreQueryFilters</c> so a
/// soft-deleted row can never silently drop and still "balance"). Sums
/// <b>functional</b> amounts of <c>Posted</c> entries only.
/// </summary>
public interface ITrialBalanceService
{
    /// <summary>
    /// Builds the trial balance for <paramref name="bookId"/> restricted to
    /// entries whose <c>EntryDate</c> falls in the inclusive
    /// [<paramref name="fromDate"/>, <paramref name="toDate"/>] range (either
    /// bound may be null for open-ended).
    /// </summary>
    Task<TrialBalance> GetTrialBalanceAsync(
        int bookId,
        DateOnly? fromDate = null,
        DateOnly? toDate = null,
        CancellationToken ct = default);
}
