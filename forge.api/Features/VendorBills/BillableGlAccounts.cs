using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.VendorBills;

/// <summary>
/// The GL accounts a standalone vendor bill line may debit directly: active, postable, non-control accounts
/// of the active book that need no job or cost-center dimension (a bill line carries neither). The same rule
/// backs the account picker and the create-time check, so the picker never offers an account the API refuses.
/// </summary>
public static class BillableGlAccounts
{
    /// <summary>The billable accounts of the active book; empty when no book is seeded.</summary>
    public static IQueryable<GlAccount> Query(AppDbContext db)
        => db.GlAccounts.AsNoTracking().Where(a =>
            a.BookId == db.Books.Where(b => b.IsActive).OrderBy(b => b.Id).Select(b => b.Id).FirstOrDefault()
            && a.IsActive
            && a.IsPostable
            && !a.IsControlAccount
            && !a.RequiresJob
            && !a.RequiresCostCenter);

    /// <summary>Throws when any of <paramref name="accountIds"/> is not billable.</summary>
    public static async Task EnsureBillableAsync(AppDbContext db, IEnumerable<int> accountIds, CancellationToken ct)
    {
        var requested = accountIds.Distinct().ToList();
        if (requested.Count == 0)
            return;

        var billable = await Query(db).Where(a => requested.Contains(a.Id)).Select(a => a.Id).ToListAsync(ct);
        var refused = requested.Except(billable).ToList();
        if (refused.Count > 0)
            throw new InvalidOperationException(
                $"GL account {string.Join(", ", refused)} cannot take a bill line: choose an active, postable, "
                + "non-control account of the active book that needs no job or cost center.");
    }
}
