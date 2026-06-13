using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities.Accounting;
using Forge.Core.Enums.Accounting;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// Standard-cost variance rollup — sums each variance account's net (debit − credit) activity over a period.
/// Because every variance posts to its own account as it arises, the lumped production variance is just the
/// SUM of these lines; there is no separate "lumped" posting mode. Read-only; gated at the controller edge.
/// </summary>
public interface IVarianceReportService
{
    Task<VarianceReportModel> GetAsync(int bookId, DateOnly from, DateOnly to, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class VarianceReportService(AppDbContext db) : IVarianceReportService
{
    // The six standard-cost variance slots + the production-variance residual catch-all, in report order.
    private static readonly (string Key, string Name)[] VarianceAccounts =
    [
        ("PURCHASE_PRICE_VARIANCE", "Material price"),
        ("MATERIAL_USAGE_VARIANCE", "Material usage"),
        ("LABOR_RATE_VARIANCE", "Labor rate"),
        ("LABOR_EFFICIENCY_VARIANCE", "Labor efficiency"),
        ("OVERHEAD_SPENDING_VARIANCE", "Overhead spending"),
        ("OVERHEAD_EFFICIENCY_VARIANCE", "Overhead efficiency"),
        ("PRODUCTION_VARIANCE", "Production (residual)"),
    ];

    public async Task<VarianceReportModel> GetAsync(int bookId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        // Resolve the determination keys → account ids for this book in one pass.
        var keys = VarianceAccounts.Select(v => v.Key).ToArray();
        var accountByKey = await db.Set<AccountDeterminationRule>()
            .Where(r => r.BookId == bookId && keys.Contains(r.Key))
            .ToDictionaryAsync(r => r.Key, r => r.GlAccountId, ct);

        var accountIds = accountByKey.Values.Distinct().ToArray();
        var lo = from; // `from`/`to` collide with LINQ query keywords inside the expression below
        var hi = to;

        // Net (debit − credit) per account over the window (debit-positive = unfavorable), Posted + Reversed.
        var balanceByAccount = await
            (from line in db.JournalLines.IgnoreQueryFilters()
             join je in db.JournalEntries.IgnoreQueryFilters() on line.JournalEntryId equals je.Id
             where je.BookId == bookId
                 && accountIds.Contains(line.GlAccountId)
                 && je.EntryDate >= lo && je.EntryDate <= hi
                 && (je.Status == JournalEntryStatus.Posted || je.Status == JournalEntryStatus.Reversed)
             group (line.Debit - line.Credit) by line.GlAccountId into g
             select new { AccountId = g.Key, Amount = g.Sum() })
            .ToDictionaryAsync(x => x.AccountId, x => x.Amount, ct);

        var lines = new List<VarianceLineModel>(VarianceAccounts.Length);
        foreach (var (key, name) in VarianceAccounts)
        {
            decimal amount = 0m;
            if (accountByKey.TryGetValue(key, out var accountId) && balanceByAccount.TryGetValue(accountId, out var bal))
                amount = decimal.Round(bal, 2);
            lines.Add(new VarianceLineModel(key, name, amount));
        }

        var total = lines.Sum(l => l.Amount);
        return new VarianceReportModel(bookId, from, to, lines, total);
    }
}
