using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// AR-002 — AR sub-ledger aging, derived from the <b>open-item sub-ledger</b>
/// (<see cref="ArOpenItem"/>): per customer, the OPEN functional amounts
/// (original − applied, both at the document's booking rate) of non-Closed /
/// non-Voided items, bucketed by the age of each item's <b>DueDate when set,
/// else DocumentDate</b>.
///
/// <para><b>Semantics change (open-item cutover).</b> Phase 1 aged at the
/// transaction grain ("balance-forward": every AR-control posting re-bucketed by
/// its EntryDate, credits landing in the bucket of the payment date). This
/// service ages at the <b>document grain</b> — the standard AR treatment: each
/// invoice's open remainder ages in the bucket of its due date, and a partial
/// payment shrinks that document's bucket rather than crediting a younger one.
/// Items not yet due (DueDate after the as-of date) sit in the youngest bucket.</para>
///
/// <para><b>Reconciliation.</b> <see cref="ReconcileAsync"/> compares the AR
/// control balance from the GL (net of all posted AR-control lines) against
/// Σ open functional amounts of the items. The items are maintained by the
/// posting services <i>inside the same transaction</i> as every control
/// movement (origination creates, applications increment at the booking-rate
/// relief, voids restore), so the two tie exactly. A non-zero difference is
/// alertable: a manual/conversion JE hitting AR control directly (bypasses
/// items BY DESIGN — the reconciliation row is what surfaces it), a legacy
/// document the backfill hasn't reconstructed, or an out-of-band mutation. A
/// <see cref="OpenItemStatus.Voided"/> item counts on neither side, matching
/// its reversed GL.</para>
///
/// <para><b>As-of.</b> The as-of date gates which documents age into the report
/// (DocumentDate on/before as-of) and the GL control balance (EntryDate on/
/// before as-of). Applied amounts are the items' CURRENT state — the open-item
/// report is a now-state sub-ledger, so a historical as-of approximates (a
/// payment dated after the as-of has already shrunk its item). The default
/// as-of (today) is exact.</para>
///
/// <para><b>Filter-immune</b> (§5.3): reads use <c>IgnoreQueryFilters</c> so a
/// soft-deleted customer master or ledger row never silently drops a balance.</para>
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

        var items = await LoadOpenItemsAsync(bookId, asOf, ct);

        // Resolve customer display names (filter-immune — a soft-deleted customer
        // still owns an open balance until it's settled).
        var customerIds = items.Select(i => i.CustomerId).Distinct().ToList();
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

        foreach (var group in items.GroupBy(i => i.CustomerId))
        {
            var bucketAmounts = new decimal[BucketDefs.Length];

            foreach (var item in group)
            {
                var idx = BucketIndexForAge(AgeDays(item, asOf));
                bucketAmounts[idx] += item.OpenFunctionalAmount;
                grandBucketTotals[idx] += item.OpenFunctionalAmount;
            }

            var openBalance = bucketAmounts.Sum();

            // A customer whose items net to zero open is not an open row; drop it
            // so the report shows only outstanding receivables.
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

        var reconciliation = await ReconcileCoreAsync(bookId, asOf, items, ct);

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

    public async Task<ArAgingReconciliation> ReconcileAsync(
        int bookId,
        DateOnly? asOfDate = null,
        CancellationToken ct = default)
    {
        var asOf = asOfDate ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        return await ReconcileCoreAsync(bookId, asOf, await LoadOpenItemsAsync(bookId, asOf, ct), ct);
    }

    /// <summary>
    /// Non-Closed / non-Voided open items for the book, restricted to documents
    /// dated on/before the as-of date. (Closed items carry 0 open; Voided items
    /// are excluded everywhere — their GL was reversed.) The date filter runs
    /// in memory so the DateTimeOffset→DateOnly comparison is provider-agnostic.
    /// </summary>
    private async Task<List<ArOpenItem>> LoadOpenItemsAsync(int bookId, DateOnly asOf, CancellationToken ct)
    {
        var items = await db.ArOpenItems.IgnoreQueryFilters().AsNoTracking()
            .Where(i => i.BookId == bookId
                && i.Status != OpenItemStatus.Closed
                && i.Status != OpenItemStatus.Voided)
            .ToListAsync(ct);

        return items
            .Where(i => DateOnly.FromDateTime(i.DocumentDate.UtcDateTime) <= asOf)
            .ToList();
    }

    /// <summary>
    /// Computes the AR-control-vs-open-items reconciliation. The control balance
    /// is the net of all posted AR-control lines from the GL; the aging total is
    /// Σ open functional amounts of the (already loaded) open items. The posting
    /// services maintain items inside every control-moving transaction, so a
    /// non-zero difference is alertable — most commonly a manual JE posted
    /// directly to AR control (which bypasses items by design and is surfaced
    /// here) or a legacy document awaiting the boot-time backfill.
    /// </summary>
    private async Task<ArAgingReconciliation> ReconcileCoreAsync(
        int bookId, DateOnly asOf, List<ArOpenItem> items, CancellationToken ct)
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
                 // Posted + Reversed headers both contribute: a Reversed original
                 // is netted by its (Posted) reversal, exactly as the trial
                 // balance treats them (§5.3).
                 && (entry.Status == JournalEntryStatus.Posted
                     || entry.Status == JournalEntryStatus.Reversed)
                 && entry.EntryDate <= asOf
             select new { line.Debit, line.FunctionalAmount })
            .ToListAsync(ct);

        // Net AR-control balance from the GL (debit-normal: Dr − Cr).
        var controlBalance = lines.Sum(l =>
            l.Debit > 0 ? l.FunctionalAmount : -l.FunctionalAmount);

        // Sub-ledger total = Σ open functional amounts of the open items.
        var agingTotal = items.Sum(i => i.OpenFunctionalAmount);

        return new ArAgingReconciliation
        {
            ControlBalance = controlBalance,
            AgingTotal = agingTotal,
        };
    }

    /// <summary>Aging anchor: DueDate when set, else DocumentDate (document-grain aging).</summary>
    private static int AgeDays(ArOpenItem item, DateOnly asOf)
    {
        var anchor = item.DueDate != default ? item.DueDate : item.DocumentDate;
        return asOf.DayNumber - DateOnly.FromDateTime(anchor.UtcDateTime).DayNumber;
    }

    private static int BucketIndexForAge(int ageDays)
    {
        // Negative age = the document is not yet due — it sits in the youngest
        // ("current") bucket.
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
}
