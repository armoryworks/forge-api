using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// Outcome of the opening-balances hard-gate evaluation (§5.5 / §7A). Logic only
/// in Phase 0 — nothing flips a capability based on this yet.
/// </summary>
/// <param name="CanEnable">True when CAP-ACCT-FULLGL may be enabled for the book.</param>
/// <param name="Reason">Human-readable explanation when <see cref="CanEnable"/> is false.</param>
public readonly record struct FullGlEnableEligibility(bool CanEnable, string? Reason);

/// <summary>
/// The CAP-ACCT-FULLGL enablement gate (ACCOUNTING_SUITE_PLAN §5.5 / §7A).
/// Implements the <b>opening-balances hard-gate</b>: the capability cannot be
/// enabled for a book until that book's opening balances are loaded
/// (go-live gate = native opening TB == external closing TB). This is the seam a
/// future capability-toggle path would call before flipping CAP-ACCT-FULLGL on.
/// <para>
/// <b>Logic only in Phase 0.</b> CAP-ACCT-FULLGL stays OFF by default and no
/// toggle path invokes this yet — the helper exists so the hard-gate rule is
/// real and testable now, not retrofitted later. It is intentionally NOT wired
/// into <c>ToggleCapabilityHandler</c> (that would be a Phase-1 step), keeping
/// the engine dark.
/// </para>
/// </summary>
public interface IGlCapabilityGate
{
    /// <summary>
    /// Evaluates whether CAP-ACCT-FULLGL may be enabled for <paramref name="bookId"/>.
    /// Returns <c>CanEnable = false</c> with a reason when the book is missing,
    /// or its opening balances have not been loaded (§7A: a posted
    /// <see cref="JournalSource.Conversion"/> opening journal).
    /// </summary>
    Task<FullGlEnableEligibility> EvaluateAsync(int bookId, CancellationToken ct = default);

    /// <summary>
    /// True when the book's opening balances are loaded — i.e. at least one
    /// <see cref="JournalEntryStatus.Posted"/> journal entry with
    /// <see cref="JournalSource.Conversion"/> exists for the book (§7A). This is
    /// how opening balances enter the ledger without adding a Phase-0 schema
    /// column; the per-book go-live TB tie-out (§7A) is a Conversion-workstream
    /// concern layered on top of this presence check.
    /// </summary>
    Task<bool> AreOpeningBalancesLoadedAsync(int bookId, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class GlCapabilityGate(AppDbContext db) : IGlCapabilityGate
{
    public async Task<bool> AreOpeningBalancesLoadedAsync(int bookId, CancellationToken ct = default)
        // IgnoreQueryFilters: ledger entities opt out of the soft-delete filter
        // (§2) and this presence check must be filter-immune for the same
        // reason the trial balance is.
        => await db.JournalEntries
            .IgnoreQueryFilters()
            .AnyAsync(
                e => e.BookId == bookId
                  && e.Source == JournalSource.Conversion
                  && e.Status == JournalEntryStatus.Posted,
                ct);

    public async Task<FullGlEnableEligibility> EvaluateAsync(int bookId, CancellationToken ct = default)
    {
        var bookExists = await db.Books.AsNoTracking().AnyAsync(b => b.Id == bookId, ct);
        if (!bookExists)
            return new FullGlEnableEligibility(false, $"Book {bookId} does not exist.");

        if (!await AreOpeningBalancesLoadedAsync(bookId, ct))
            return new FullGlEnableEligibility(
                false,
                $"CAP-ACCT-FULLGL cannot be enabled for book {bookId}: opening balances are not loaded. " +
                "Load the opening journal (Source=Conversion) and pass the go-live TB tie-out first (§7A).");

        return new FullGlEnableEligibility(true, null);
    }
}
