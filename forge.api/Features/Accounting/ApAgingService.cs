using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// AP-001 — Accounts-Payable aging (the AP counterpart of <see cref="ArAgingService"/>), derived
/// from the <b>open-item sub-ledger</b> (<see cref="ApOpenItem"/>): per vendor, the OPEN functional
/// amounts (original − applied, both at the document's booking rate) of non-Closed / non-Voided
/// items, bucketed by the age of each item's <b>DueDate when set, else DocumentDate</b>.
/// <para>
/// <b>Semantics change (open-item cutover).</b> Phase 2 aged at the transaction grain
/// (balance-forward: every AP-control posting re-bucketed by its EntryDate). This service ages at
/// the <b>document grain</b> — the standard AP treatment: each bill's open remainder ages in the
/// bucket of its due date, and a partial payment shrinks that document's bucket rather than
/// crediting a younger one. Items not yet due sit in the youngest bucket.
/// </para>
/// <para>
/// <b>Reconciliation</b> compares the AP control balance from the GL against Σ open functional
/// amounts of the items, which the posting services maintain inside every control-moving
/// transaction — so the two tie exactly. A non-zero difference is alertable (manual JE hitting AP
/// control directly — bypasses items BY DESIGN, surfaced here; vendor-settled <c>Expense</c>
/// postings, which credit AP control without a bill document; or a legacy document awaiting the
/// boot-time backfill). A Voided item (voided bill — its GL reversed) counts on neither side.
/// <b>As-of</b> gates document/entry inclusion; applied amounts are the items' current state, so
/// the default as-of (today) is exact and a historical as-of approximates. <b>Filter-immune:</b>
/// reads use <c>IgnoreQueryFilters()</c>.
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

        var items = await LoadOpenItemsAsync(bookId, asOf, ct);

        var vendorIds = items.Select(i => i.VendorId).Distinct().ToList();
        var vendorNames = await db.Set<Vendor>()
            .IgnoreQueryFilters()
            .Where(v => vendorIds.Contains(v.Id))
            .Select(v => new { v.Id, v.CompanyName })
            .ToDictionaryAsync(v => v.Id, v => v.CompanyName, ct);

        var vendorRows = new List<ApAgingVendorRow>();
        var grandBucketTotals = new decimal[BucketDefs.Length];

        foreach (var group in items.GroupBy(i => i.VendorId))
        {
            var bucketAmounts = new decimal[BucketDefs.Length];
            foreach (var item in group)
            {
                var idx = BucketIndexForAge(AgeDays(item, asOf));
                bucketAmounts[idx] += item.OpenFunctionalAmount;
                grandBucketTotals[idx] += item.OpenFunctionalAmount;
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

        var reconciliation = await ReconcileCoreAsync(bookId, asOf, items, ct);

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
        return await ReconcileCoreAsync(bookId, asOf, await LoadOpenItemsAsync(bookId, asOf, ct), ct);
    }

    /// <summary>
    /// Non-Closed / non-Voided open items for the book, restricted to documents dated on/before
    /// the as-of date. (Closed items carry 0 open; Voided items are excluded everywhere — their GL
    /// was reversed.) Date filter in memory so the DateTimeOffset→DateOnly comparison is
    /// provider-agnostic.
    /// </summary>
    private async Task<List<ApOpenItem>> LoadOpenItemsAsync(int bookId, DateOnly asOf, CancellationToken ct)
    {
        var items = await db.ApOpenItems.IgnoreQueryFilters().AsNoTracking()
            .Where(i => i.BookId == bookId
                && i.Status != OpenItemStatus.Closed
                && i.Status != OpenItemStatus.Voided)
            .ToListAsync(ct);

        return items
            .Where(i => DateOnly.FromDateTime(i.DocumentDate.UtcDateTime) <= asOf)
            .ToList();
    }

    private async Task<ApAgingReconciliation> ReconcileCoreAsync(
        int bookId, DateOnly asOf, List<ApOpenItem> items, CancellationToken ct)
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
             select new { line.Credit, line.FunctionalAmount })
            .ToListAsync(ct);

        // Net AP-control balance from the GL (credit-normal: Cr − Dr).
        var controlBalance = lines.Sum(l =>
            l.Credit > 0 ? l.FunctionalAmount : -l.FunctionalAmount);

        // Sub-ledger total = Σ open functional amounts of the open items.
        var agingTotal = items.Sum(i => i.OpenFunctionalAmount);

        return new ApAgingReconciliation { ControlBalance = controlBalance, AgingTotal = agingTotal };
    }

    /// <summary>Aging anchor: DueDate when set, else DocumentDate (document-grain aging).</summary>
    private static int AgeDays(ApOpenItem item, DateOnly asOf)
    {
        var anchor = item.DueDate != default ? item.DueDate : item.DocumentDate;
        return asOf.DayNumber - DateOnly.FromDateTime(anchor.UtcDateTime).DayNumber;
    }

    private static int BucketIndexForAge(int ageDays)
    {
        // Negative age = not yet due — the youngest ("current") bucket.
        if (ageDays < 0)
            return 0;

        for (var i = 0; i < BucketDefs.Length; i++)
        {
            var (from, to, _) = BucketDefs[i];
            if (ageDays >= from && (to is null || ageDays <= to))
                return i;
        }

        return BucketDefs.Length - 1;
    }

    private static IReadOnlyList<ApAgingBucket> BuildBuckets(decimal[] amounts)
        => BucketDefs
            .Select((d, i) => new ApAgingBucket { FromDays = d.From, ToDays = d.To, Label = d.Label, Amount = amounts[i] })
            .ToList();
}
