namespace Forge.Core.Interfaces;

/// <summary>
/// Allocates the next monotonic <c>JournalEntry.EntryNumber</c> for a
/// <c>(BookId, FiscalYearId)</c> counter. Implemented with a row-locked
/// <c>UPDATE acct_number_sequences … RETURNING</c> (the safe
/// <c>JobRepository</c> pattern, NOT <c>InvoiceRepository</c>'s read-max+1) so
/// concurrent posters never collide. Gaps are allowed and documented (§5.1).
/// <para>
/// Abstracted behind an interface so the posting engine stays provider-neutral
/// and unit-testable (the InMemory test provider cannot execute the row-lock
/// SQL; tests supply an in-process allocator).
/// </para>
/// </summary>
public interface IAcctNumberSequenceAllocator
{
    /// <summary>
    /// Atomically increments and returns the next <c>EntryNumber</c> for the
    /// given book/year, creating the counter row on first use. Must run inside
    /// the caller's transaction so the allocation rolls back with a failed post.
    /// </summary>
    Task<long> AllocateNextAsync(int bookId, int fiscalYearId, CancellationToken ct = default);
}
