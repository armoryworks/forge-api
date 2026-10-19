using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities.Accounting;
using Forge.Core.Enums.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// Resolves the GL account a posting should hit from an explicit account id or, failing that, a book-level
/// determination key. An explicit id must be an active, postable, non-control account of an allowed type in the
/// book (else a validation error on <c>field</c>); a keyed account of the wrong type counts as unresolved, so a
/// hand-made account that happens to carry the key is never posted to by mistake.
/// </summary>
public static class PostableGlAccountLookup
{
    public static async Task<GlAccount?> ResolveAsync(
        AppDbContext db,
        int bookId,
        int? accountId,
        string? fallbackKey,
        string field,
        IReadOnlyCollection<AccountType> allowedTypes,
        CancellationToken ct)
    {
        if (accountId is int id)
        {
            var account = await db.GlAccounts.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == id && a.BookId == bookId, ct);
            if (account is null || !IsUsable(account, allowedTypes))
                throw new ValidationException([new ValidationFailure(field,
                    $"GL account {id} must be an active, postable {string.Join(" or ", allowedTypes)} account in this book.")]);
            return account;
        }

        if (fallbackKey is null)
            return null;

        var keyed = await (
            from rule in db.AccountDeterminationRules.AsNoTracking()
            join a in db.GlAccounts.AsNoTracking() on rule.GlAccountId equals a.Id
            where rule.BookId == bookId
                && rule.Key == fallbackKey
                && rule.ItemId == null
                && rule.CategoryId == null
                && rule.ValuationClassId == null
            select a).FirstOrDefaultAsync(ct);

        return keyed is not null && IsUsable(keyed, allowedTypes) ? keyed : null;
    }

    private static bool IsUsable(GlAccount account, IReadOnlyCollection<AccountType> allowedTypes) =>
        account.IsActive && account.IsPostable && !account.IsControlAccount && allowedTypes.Contains(account.AccountType);
}
