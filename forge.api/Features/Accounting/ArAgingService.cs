using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// Phase-1 STAGE D — AR sub-ledger + aging, derived from the ledger
/// (ACCOUNTING_SUITE_PLAN §6 Phase-1 row "AR sub-ledger + aging", §7 matrix rows
/// 1–3, §9 "sub-ledger↔control reconciliation").
///
/// <para>The sub-ledger is <b>not a parallel store</b>: it is projected directly
/// from posted <see cref="Forge.Core.Entities.Accounting.JournalLine"/>s on
/// AR-control GL accounts (<c>GlAccount.ControlType == AR</c>) that carry a
/// <c>SubledgerPartyType = Customer</c> party. Because it is the same data the
/// trial balance reads, the AR-control-vs-aging reconciliation
/// (<see cref="ReconcileAsync"/>) ties by construction; any non-zero difference
/// is a genuine defect (an AR-control posting missing its customer party, which
/// the posting engine rejects on control lines — §5.2 — or an out-of-band
/// mutation past the immutability interceptor/trigger).</para>
///
/// <para><b>Filter-immune</b> (§5.3): every read uses <c>IgnoreQueryFilters</c>
/// so a soft-deleted customer master or ledger row never silently drops and
/// makes the sub-ledger appear to reconcile when it does not.</para>
///
/// <para><b>Aging signing.</b> On an AR (debit-normal) control account a debit
/// raises the customer's open balance (an invoice) and a credit lowers it (a
/// payment / credit applied to that customer). We net <c>Debit − Credit</c> per
/// posting and bucket by the age of the posting's <c>EntryDate</c>. Phase 1 ages
/// at the transaction grain (not FIFO invoice-by-invoice application), which is
/// the standard "balance-forward" aging; precise open-item application reporting
/// rides on the open-item sub-ledger load (§7A) and is a later refinement.</para>
/// </summary>
public sealed class ArAgingService(AppDbContext db, IClock clock) : IArAgingService
{
    // Standard 30-day aging ladder: 0-30, 31-60, 61-90, 91+. Each tuple is the
    // inclusive lower bound, inclusive upper bound (null = open-ended), label.
    private static readonly (int From, int? To, string Label)[] BucketDefs =
    [
        (0, 30, "0-30"),
        (31, 60, "31-60"),
        (61, 90, "61-90"),
        (91, null, "91+"),
    ];

    public async Task<ArAging> GetArAgingAsync(
        int bookId,
        DateOnly? asOfDate = null,
        CancellationToken ct = default)
    {
        var asOf = asOfDate ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

        // Project posted AR-control lines carrying a Customer party. We pull the
        // raw rows (EntryDate + net amount + customer id) and bucket in memory so
        // the age arithmetic is provider-agnostic (InMemory can't do DateOnly
        // day-diff in SQL) and provably correct.
        var postings = await
            (from line in db.JournalLines.IgnoreQueryFilters()
             join entry in db.JournalEntries.IgnoreQueryFilters()
                 on line.JournalEntryId equals entry.Id
             join account in db.GlAccounts.IgnoreQueryFilters()
                 on line.GlAccountId equals account.Id
             where entry.BookId == bookId
                 && account.ControlType == ControlAccountType.AR
                 && account.IsControlAccount
                 && line.SubledgerPartyType == SubledgerPartyType.Customer
                 && line.SubledgerPartyId != null
                 // Posted + Reversed headers both contribute: a Reversed original
                 // is netted by its (Posted) reversal, exactly as the trial
                 // balance treats them (§5.3).
                 && (entry.Status == JournalEntryStatus.Posted
                     || entry.Status == JournalEntryStatus.Reversed)
                 // Only entries dated on/before the as-of date age into the report.
                 && entry.EntryDate <= asOf
             select new ArPostingRow
             {
                 CustomerId = line.SubledgerPartyId!.Value,
                 EntryDate = entry.EntryDate,
                 // Functional amount, debit-positive (AR is debit-normal).
                 NetFunctional = line.Debit > 0 ? line.FunctionalAmount : -line.FunctionalAmount,
             })
            .ToListAsync(ct);

        // Resolve customer display names (filter-immune — a soft-deleted customer
        // still owns an open balance until it's settled).
        var customerIds = postings.Select(p => p.CustomerId).Distinct().ToList();
        var customerNames = await db.Set<Customer>()
            .IgnoreQueryFilters()
            .Where(c => customerIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Name, c.CompanyName })
            .ToDictionaryAsync(
                c => c.Id,
                c => string.IsNullOrWhiteSpace(c.CompanyName) ? c.Name : c.CompanyName!,
                ct);

        var customerRows = new List<ArAgingCustomerRow>();
        var grandBucketTotals = new decimal[BucketDefs.Length];

        foreach (var group in postings.GroupBy(p => p.CustomerId))
        {
            var bucketAmounts = new decimal[BucketDefs.Length];

            foreach (var posting in group)
            {
                var ageDays = asOf.DayNumber - posting.EntryDate.DayNumber;
                var idx = BucketIndexForAge(ageDays);
                bucketAmounts[idx] += posting.NetFunctional;
                grandBucketTotals[idx] += posting.NetFunctional;
            }

            var openBalance = bucketAmounts.Sum();

            // A customer that nets to zero (fully paid) is not an open item; drop
            // it from the aging so the report shows only outstanding receivables.
            if (openBalance == 0m)
                continue;

            customerRows.Add(new ArAgingCustomerRow
            {
                CustomerId = group.Key,
                CustomerName = customerNames.TryGetValue(group.Key, out var name)
                    ? name
                    : $"Customer {group.Key}",
                OpenBalance = openBalance,
                Buckets = BuildBuckets(bucketAmounts),
            });
        }

        var reconciliation = await ReconcileCoreAsync(bookId, asOf, ct);

        return new ArAging
        {
            BookId = bookId,
            AsOfDate = asOf,
            Customers = customerRows
                .OrderByDescending(r => r.OpenBalance)
                .ThenBy(r => r.CustomerName)
                .ToList(),
            TotalsByBucket = BuildBuckets(grandBucketTotals),
            GrandTotal = customerRows.Sum(r => r.OpenBalance),
            Reconciliation = reconciliation,
        };
    }

    public Task<ArAgingReconciliation> ReconcileAsync(
        int bookId,
        DateOnly? asOfDate = null,
        CancellationToken ct = default)
    {
        var asOf = asOfDate ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        return ReconcileCoreAsync(bookId, asOf, ct);
    }

    /// <summary>
    /// Computes the AR-control-vs-aging reconciliation. The control balance is the
    /// net of <b>all</b> posted AR-control lines (with or without a party); the
    /// aging total is the net of those carrying a Customer party. They tie when
    /// every AR-control posting has a customer party — which the engine enforces
    /// on control lines (§5.2) — so a non-zero difference is alertable.
    /// </summary>
    private async Task<ArAgingReconciliation> ReconcileCoreAsync(
        int bookId, DateOnly asOf, CancellationToken ct)
    {
        var lines = await
            (from line in db.JournalLines.IgnoreQueryFilters()
             join entry in db.JournalEntries.IgnoreQueryFilters()
                 on line.JournalEntryId equals entry.Id
             join account in db.GlAccounts.IgnoreQueryFilters()
                 on line.GlAccountId equals account.Id
             where entry.BookId == bookId
                 && account.ControlType == ControlAccountType.AR
                 && account.IsControlAccount
                 && (entry.Status == JournalEntryStatus.Posted
                     || entry.Status == JournalEntryStatus.Reversed)
                 && entry.EntryDate <= asOf
             select new
             {
                 line.Debit,
                 line.Credit,
                 line.FunctionalAmount,
                 line.SubledgerPartyType,
                 line.SubledgerPartyId,
             })
            .ToListAsync(ct);

        // Net AR-control balance from the GL (debit-normal: Dr − Cr).
        var controlBalance = lines.Sum(l =>
            l.Debit > 0 ? l.FunctionalAmount : -l.FunctionalAmount);

        // Aging total = the customer-attributed slice (what the sub-ledger projects).
        var agingTotal = lines
            .Where(l => l.SubledgerPartyType == SubledgerPartyType.Customer && l.SubledgerPartyId != null)
            .Sum(l => l.Debit > 0 ? l.FunctionalAmount : -l.FunctionalAmount);

        return new ArAgingReconciliation
        {
            ControlBalance = controlBalance,
            AgingTotal = agingTotal,
        };
    }

    private static int BucketIndexForAge(int ageDays)
    {
        // Negative age (a future-dated entry on/before the as-of cutoff cannot be
        // negative here, but guard anyway) lands in the youngest bucket.
        if (ageDays < 0)
            return 0;

        for (var i = 0; i < BucketDefs.Length; i++)
        {
            var (from, to, _) = BucketDefs[i];
            if (ageDays >= from && (to is null || ageDays <= to))
                return i;
        }

        // Unreachable (the last bucket is open-ended), but fall back to the oldest.
        return BucketDefs.Length - 1;
    }

    private static List<ArAgingBucket> BuildBuckets(decimal[] amounts)
    {
        var buckets = new List<ArAgingBucket>(BucketDefs.Length);
        for (var i = 0; i < BucketDefs.Length; i++)
        {
            var (from, to, label) = BucketDefs[i];
            buckets.Add(new ArAgingBucket
            {
                FromDays = from,
                ToDays = to,
                Label = label,
                Amount = amounts[i],
            });
        }
        return buckets;
    }

    /// <summary>Flat projection of an AR-control posting for in-memory aging.</summary>
    private sealed class ArPostingRow
    {
        public int CustomerId { get; init; }
        public DateOnly EntryDate { get; init; }
        public decimal NetFunctional { get; init; }
    }
}
