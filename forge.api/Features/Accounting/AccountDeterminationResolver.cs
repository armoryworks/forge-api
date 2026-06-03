using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// Resolves business-event determination keys to GL accounts via the
/// <c>(BookId, Key[, scope])</c> map (§4, §5.2). Most-specific scope wins; there
/// is <b>no</b> silent cross-book fallback. An unmapped / non-postable /
/// inactive / cross-book key is surfaced as a hard <see cref="PostingException"/>.
/// </summary>
public sealed class AccountDeterminationResolver(AppDbContext db) : IAccountDeterminationResolver
{
    public async Task<GlAccount> ResolveAsync(
        int bookId,
        string key,
        CancellationToken ct = default,
        int? itemId = null,
        int? categoryId = null,
        int? valuationClassId = null)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new PostingException("DETERMINATION_KEY_EMPTY", "A determination key is required.");

        // Candidate rules for this book + key. Scope columns are global (null) in
        // Phase 0; honor more-specific scopes when supplied so Phase-2 scoping is
        // config, not a migration.
        var candidates = await db.AccountDeterminationRules
            .AsNoTracking()
            .Where(r => r.BookId == bookId && r.Key == key)
            .ToListAsync(ct);

        // Most-specific scope wins: rank by how many scope columns match the
        // request (a row whose scope column is set must match the supplied arg;
        // a null scope column is a wildcard that always matches but scores 0).
        var best = candidates
            .Where(r => ScopeMatches(r.ItemId, itemId)
                     && ScopeMatches(r.CategoryId, categoryId)
                     && ScopeMatches(r.ValuationClassId, valuationClassId))
            .OrderByDescending(Specificity)
            .FirstOrDefault();

        if (best is null)
            throw new PostingException(
                "DETERMINATION_UNMAPPED",
                $"No account-determination rule resolves key '{key}' for book {bookId}.");

        var account = await db.GlAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == best.GlAccountId, ct);

        if (account is null)
            throw new PostingException(
                "DETERMINATION_ACCOUNT_MISSING",
                $"Determination rule for key '{key}' (book {bookId}) points at missing account {best.GlAccountId}.");

        ValidateTarget(bookId, key, account);
        return account;
    }

    public async Task<IReadOnlyList<string>> ValidateKeysAsync(
        int bookId,
        IReadOnlyCollection<string> keys,
        CancellationToken ct = default)
    {
        var failed = new List<string>();
        foreach (var key in keys)
        {
            try
            {
                await ResolveAsync(bookId, key, ct);
            }
            catch (PostingException)
            {
                failed.Add(key);
            }
        }
        return failed;
    }

    /// <summary>A null rule scope is a wildcard; otherwise it must equal the supplied arg.</summary>
    private static bool ScopeMatches(int? ruleScope, int? requested)
        => ruleScope is null || ruleScope == requested;

    private static int Specificity(AccountDeterminationRule r)
        => (r.ItemId.HasValue ? 1 : 0)
         + (r.CategoryId.HasValue ? 1 : 0)
         + (r.ValuationClassId.HasValue ? 1 : 0);

    private static void ValidateTarget(int bookId, string key, GlAccount account)
    {
        if (account.BookId != bookId)
            throw new PostingException(
                "DETERMINATION_CROSS_BOOK",
                $"Determination key '{key}' resolves to account {account.AccountNumber} in book {account.BookId}, " +
                $"not the posting book {bookId}. Cross-book resolution is forbidden.");

        if (!account.IsPostable)
            throw new PostingException(
                "DETERMINATION_NOT_POSTABLE",
                $"Determination key '{key}' resolves to non-postable account {account.AccountNumber}.");

        if (!account.IsActive)
            throw new PostingException(
                "DETERMINATION_INACTIVE",
                $"Determination key '{key}' resolves to inactive account {account.AccountNumber}.");
    }
}
