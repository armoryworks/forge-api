using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums.Accounting;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// §7A opening inventory belongs in a costed stock load, not the opening journal: a journal line on an
/// inventory control account would put value in the ledger with no quantity or valuation behind it. Both opening
/// paths (the CSV opening journal and the opening-balances import) call this before posting a new journal.
/// </summary>
internal static class OpeningInventoryGuard
{
    internal const string Code = "OPENING_INVENTORY_USE_STOCK_LOAD";

    /// <summary>
    /// Throws <see cref="PostingException"/> <see cref="Code"/> when any of <paramref name="accountIds"/> (or the
    /// accounts <paramref name="determinationKeys"/> map to in the book) is an inventory control account.
    /// </summary>
    public static async Task EnsureNoInventoryLinesAsync(
        AppDbContext db, int bookId, IEnumerable<int> accountIds, IEnumerable<string> determinationKeys, CancellationToken ct)
    {
        var ids = accountIds.ToHashSet();
        var keys = determinationKeys.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct().ToList();
        if (keys.Count > 0)
        {
            var mapped = await db.AccountDeterminationRules.AsNoTracking()
                .Where(r => r.BookId == bookId && keys.Contains(r.Key))
                .Select(r => r.GlAccountId)
                .ToListAsync(ct);
            ids.UnionWith(mapped);
        }
        if (ids.Count == 0)
            return;

        var inventory = await db.GlAccounts.AsNoTracking().IgnoreQueryFilters()
            .Where(a => ids.Contains(a.Id) && a.IsControlAccount && a.ControlType == ControlAccountType.Inventory)
            .OrderBy(a => a.AccountNumber)
            .Select(a => new { a.AccountNumber, a.Name })
            .ToListAsync(ct);
        if (inventory.Count == 0)
            return;

        var named = string.Join(", ", inventory.Select(a => $"{a.AccountNumber} {a.Name}"));
        var verb = inventory.Count == 1 ? "is an inventory control account" : "are inventory control accounts";
        throw new PostingException(
            Code,
            $"Account {named} {verb}. Load opening inventory as a costed stock load (Accounting > Opening stock) " +
            "so on-hand quantities and the valuation match the ledger; post the rest of the opening journal with " +
            "Opening Balance Equity as the offset.");
    }
}
