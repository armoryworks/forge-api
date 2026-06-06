using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// Phase-2 STAGE A — Accounts-Payable aging (the AP counterpart of <see cref="ArAgingService"/>).
/// Derives the aging from posted AP-control <c>JournalLine</c>s carrying a <b>Vendor</b> party, bucketed
/// by age at the <c>JournalEntry.EntryDate</c> grain (balance-forward, mirroring AR — NOT FIFO open-item
/// application; precise open-item aging rides on the §7A open-item sub-ledger load later).
/// <para>
/// <b>Sign:</b> AP is credit-normal — a bill (credit to AP control) raises the open payable and a payment
/// (debit) lowers it, so the netting is <c>Credit − Debit</c> (credit-positive), the mirror-image of AR's
/// <c>Debit − Credit</c>. <b>Filter-immune:</b> every join uses <c>IgnoreQueryFilters()</c> so soft-deleted
/// ledger rows never silently drop a balance. Posted AND Reversed entries both contribute (a reversed
/// original is netted by its reversal), matching the trial balance.
/// </para>
/// </summary>
public sealed class ApAgingService(AppDbContext db, IClock clock) : IApAgingService
{
    private static readonly (int From, int? To, string Label)[] BucketDefs =
    [
        (0, 30, "0-30"),
        (31, 60, "31-60"),
        (61, 90, "61-90"),
        (91, null, "91+"),
    ];

    public async Task<ApAging> GetApAgingAsync(int bookId, DateOnly? asOfDate = null, CancellationToken ct = default)
    {
        var asOf = asOfDate ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

        var postings = await
            (from line in db.JournalLines.IgnoreQueryFilters()
             join entry in db.JournalEntries.IgnoreQueryFilters()
                 on line.JournalEntryId equals entry.Id
             join account in db.GlAccounts.IgnoreQueryFilters()
                 on line.GlAccountId equals account.Id
             where entry.BookId == bookId
                 && account.ControlType == ControlAccountType.AP
                 && account.IsControlAccount
                 && line.SubledgerPartyType == SubledgerPartyType.Vendor
                 && line.SubledgerPartyId != null
                 && (entry.Status == JournalEntryStatus.Posted
                     || entry.Status == JournalEntryStatus.Reversed)
                 && entry.EntryDate <= asOf
             select new ApPostingRow
             {
                 VendorId = line.SubledgerPartyId!.Value,
                 EntryDate = entry.EntryDate,
                 // Functional amount, credit-positive (AP is credit-normal).
                 NetFunctional = line.Credit > 0 ? line.FunctionalAmount : -line.FunctionalAmount,
             })
            .ToListAsync(ct);

        var vendorIds = postings.Select(p => p.VendorId).Distinct().ToList();
        var vendorNames = await db.Set<Vendor>()
            .IgnoreQueryFilters()
            .Where(v => vendorIds.Contains(v.Id))
            .Select(v => new { v.Id, v.CompanyName })
            .ToDictionaryAsync(v => v.Id, v => v.CompanyName, ct);

        var vendorRows = new List<ApAgingVendorRow>();
        var grandBucketTotals = new decimal[BucketDefs.Length];

        foreach (var group in postings.GroupBy(p => p.VendorId))
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
            if (openBalance == 0m)
                continue; // fully-settled vendor drops off the report

            vendorRows.Add(new ApAgingVendorRow
            {
                VendorId = group.Key,
                VendorName = vendorNames.TryGetValue(group.Key, out var name) ? name : $"Vendor {group.Key}",
                OpenBalance = openBalance,
                Buckets = BuildBuckets(bucketAmounts),
            });
        }

        var reconciliation = await ReconcileCoreAsync(bookId, asOf, ct);

        return new ApAging
        {
            BookId = bookId,
            AsOfDate = asOf,
            Vendors = vendorRows
                .OrderByDescending(r => r.OpenBalance)
                .ThenBy(r => r.VendorName)
                .ToList(),
            TotalsByBucket = BuildBuckets(grandBucketTotals),
            GrandTotal = vendorRows.Sum(r => r.OpenBalance),
            Reconciliation = reconciliation,
        };
    }

    public async Task<ApAgingReconciliation> ReconcileAsync(int bookId, DateOnly? asOfDate = null, CancellationToken ct = default)
    {
        var asOf = asOfDate ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        return await ReconcileCoreAsync(bookId, asOf, ct);
    }

    private async Task<ApAgingReconciliation> ReconcileCoreAsync(int bookId, DateOnly asOf, CancellationToken ct)
    {
        var lines = await
            (from line in db.JournalLines.IgnoreQueryFilters()
             join entry in db.JournalEntries.IgnoreQueryFilters() on line.JournalEntryId equals entry.Id
             join account in db.GlAccounts.IgnoreQueryFilters() on line.GlAccountId equals account.Id
             where entry.BookId == bookId
                 && account.ControlType == ControlAccountType.AP
                 && account.IsControlAccount
                 && (entry.Status == JournalEntryStatus.Posted || entry.Status == JournalEntryStatus.Reversed)
                 && entry.EntryDate <= asOf
             select new { line.Debit, line.Credit, line.FunctionalAmount, line.SubledgerPartyType, line.SubledgerPartyId })
            .ToListAsync(ct);

        // Net AP-control balance from the GL (credit-normal: Cr − Dr).
        var controlBalance = lines.Sum(l =>
            l.Credit > 0 ? l.FunctionalAmount : -l.FunctionalAmount);

        // Aging total = the vendor-attributed slice (what the sub-ledger projects).
        var agingTotal = lines
            .Where(l => l.SubledgerPartyType == SubledgerPartyType.Vendor && l.SubledgerPartyId != null)
            .Sum(l => l.Credit > 0 ? l.FunctionalAmount : -l.FunctionalAmount);

        return new ApAgingReconciliation { ControlBalance = controlBalance, AgingTotal = agingTotal };
    }

    private static int BucketIndexForAge(int ageDays)
    {
        for (var i = 0; i < BucketDefs.Length; i++)
        {
            var (from, to, _) = BucketDefs[i];
            if (ageDays >= from && (to is null || ageDays <= to))
                return i;
        }

        // age < 0 (future-dated; excluded by the EntryDate<=asOf filter) — clamp into the first bucket.
        return 0;
    }

    private static IReadOnlyList<ApAgingBucket> BuildBuckets(decimal[] amounts)
        => BucketDefs
            .Select((d, i) => new ApAgingBucket { FromDays = d.From, ToDays = d.To, Label = d.Label, Amount = amounts[i] })
            .ToList();

    private sealed class ApPostingRow
    {
        public int VendorId { get; init; }
        public DateOnly EntryDate { get; init; }
        public decimal NetFunctional { get; init; }
    }
}
