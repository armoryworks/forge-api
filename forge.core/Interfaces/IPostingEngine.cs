using Forge.Core.Entities.Accounting;
using Forge.Core.Models.Accounting;

namespace Forge.Core.Interfaces;

/// <summary>
/// The single write seam into the general ledger (§2, §4). Operational command
/// handlers call <see cref="PostAsync"/> <b>inline, in their own transaction</b>
/// — the operational change and the journal entry commit (or roll back)
/// together. When <c>CAP-ACCT-FULLGL</c> is off for the book, posting is a
/// no-op at the call site (the engine itself always posts when invoked).
/// <para>
/// The engine validates everything in §5.2, writes an immutable
/// <see cref="JournalEntry"/> with <see cref="Enums.Accounting.JournalEntryStatus.Posted"/>,
/// assigns the <c>EntryNumber</c> from a row-locked counter, and maintains the
/// incremental <see cref="LedgerBalance"/> read-model in the same transaction.
/// </para>
/// </summary>
public interface IPostingEngine
{
    /// <summary>
    /// Validates and posts a balanced double-entry request. A duplicate
    /// <c>(BookId, IdempotencyKey)</c> returns the existing entry (no throw).
    /// Adds the entry + lines to the shared request-scoped context and calls
    /// <c>SaveChangesAsync</c> so the write participates in the caller's
    /// transaction.
    /// </summary>
    /// <param name="request">The balanced posting request.</param>
    /// <param name="postedByUserId">
    /// Server-trusted principal id (never client-supplied) recorded as
    /// <c>PostedBy</c>.
    /// </param>
    /// <exception cref="PostingException">Any §5.2 validation failure.</exception>
    Task<JournalEntry> PostAsync(PostingRequest request, int postedByUserId, CancellationToken ct = default);

    /// <summary>
    /// Maker-checker async path (§5.7): fully validates a balanced request (same §5.2 checks as
    /// <see cref="PostAsync"/>) and writes the entry with status
    /// <see cref="Enums.Accounting.JournalEntryStatus.PendingApproval"/> — its <c>EntryNumber</c> is
    /// allocated (gaps are allowed) but it is <b>NOT</b> applied to the <see cref="LedgerBalance"/> read-model,
    /// so it does not affect the trial balance until a distinct approver finalizes it via
    /// <see cref="ApprovePendingAsync"/>. <c>PostedBy</c> records the submitter (the maker).
    /// </summary>
    /// <exception cref="PostingException">Any §5.2 validation failure.</exception>
    Task<JournalEntry> PostPendingAsync(PostingRequest request, int submittedByUserId, CancellationToken ct = default);

    /// <summary>
    /// Finalizes a <see cref="Enums.Accounting.JournalEntryStatus.PendingApproval"/> entry: re-checks the
    /// fiscal period (rejects HardClosed), flips it to <c>Posted</c>, records the approver (who MUST differ
    /// from the submitting <c>PostedBy</c>, §5.7) and <c>PostedAt</c>, and applies it to the
    /// <see cref="LedgerBalance"/> read-model — all in the caller's transaction.
    /// </summary>
    /// <exception cref="PostingException">
    /// Not found, not pending, approver-not-distinct, or a period violation.
    /// </exception>
    Task<JournalEntry> ApprovePendingAsync(long entryId, int approvedByUserId, CancellationToken ct = default);

    /// <summary>
    /// Reverses a posted entry by posting an equal-and-opposite entry and
    /// flipping the original to <see cref="Enums.Accounting.JournalEntryStatus.Reversed"/>
    /// with a <c>ReversedByEntryId</c> link, in one transaction.
    /// <para>
    /// Preconditions (§5.2): the original is <c>Posted</c> AND its
    /// <c>ReversedByEntryId</c> is null (no double-reverse). The reversal's
    /// period is resolved from <paramref name="reversalDate"/> (its own date)
    /// and rejected if HardClosed.
    /// </para>
    /// </summary>
    /// <exception cref="PostingException">Precondition or period violation.</exception>
    Task<JournalEntry> ReverseAsync(
        long entryId,
        DateOnly reversalDate,
        string reason,
        int reversedByUserId,
        CancellationToken ct = default);
}
