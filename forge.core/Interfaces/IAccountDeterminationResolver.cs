using Forge.Core.Entities.Accounting;

namespace Forge.Core.Interfaces;

/// <summary>
/// Resolves a business-event determination <c>Key</c> to a concrete
/// <see cref="GlAccount"/> for a book (§4 account-determination map, §5.2).
/// Business events never hardcode accounts; they pass a key
/// (e.g. <c>SALES_REVENUE</c>) and the resolver maps it via
/// <c>(BookId, Key[, scope])</c> with most-specific-scope-wins precedence and
/// <b>no</b> silent cross-book fallback.
/// </summary>
public interface IAccountDeterminationResolver
{
    /// <summary>
    /// Resolves <paramref name="key"/> to a postable, active, in-book account.
    /// An unmapped / non-postable / cross-book / inactive key is a hard,
    /// alertable error (the engine surfaces it as a <c>PostingException</c>).
    /// Phase-0 callers pass no scope (global rows only); Phase-2 scope args are
    /// honored when present (most-specific wins).
    /// </summary>
    Task<GlAccount> ResolveAsync(
        int bookId,
        string key,
        CancellationToken ct = default,
        int? itemId = null,
        int? categoryId = null,
        int? valuationClassId = null);

    /// <summary>
    /// Startup / seed-time validator (§5.2): asserts every supplied key resolves
    /// to a postable, active, in-book account for the book. Returns the keys
    /// that fail to resolve (empty = all good) so misconfiguration is caught
    /// before the first posting, not on the hot path. Does not throw.
    /// </summary>
    Task<IReadOnlyList<string>> ValidateKeysAsync(
        int bookId,
        IReadOnlyCollection<string> keys,
        CancellationToken ct = default);
}
