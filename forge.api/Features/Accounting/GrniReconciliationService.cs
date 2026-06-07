using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// Phase-2 STAGE D.3 — GRNI (Goods-Received-Not-Invoiced) reconciliation + aging. See
/// <see cref="GrniReconciliation"/> for the model. Read-only: it derives everything from the ledger and the
/// existing receive/bill state, mutating nothing.
///
/// <para><b>Two sources, one truth.</b> The <b>GL balance</b> is the net of the GRNI account(s) (resolved
/// from the <c>GRNI</c> determination key), credit-positive, Posted + Reversed, on/before <c>AsOf</c>. The
/// <b>operational open</b> is Σ <c>UnbilledReceivedQuantity × PO UnitPrice</c> over open PO lines — exactly
/// what each receipt accrued (STAGE C, Cr GRNI base) and each 3-way-match bill clears (STAGE D.2, Dr GRNI),
/// so the two agree by construction when every receipt was posted and every bill matched. The variance is
/// the §12 control.</para>
///
/// <para><b>Simplifications / caveats (ratify):</b>
/// <list type="bullet">
///   <item>The operational side uses <i>current</i> received/billed quantities <i>and current PO unit
///         prices</i> (not an as-of replay), so a back-dated <c>AsOf</c> compares a historical GL balance to
///         the present operational position — call it with <c>AsOf = today</c> for a true reconciliation. If
///         a PO line's <c>UnitPrice</c> is edited after a receipt accrued at the old price, GL and operational
///         legitimately diverge (the variance surfaces it — which is arguably the point).</item>
///   <item>Aging ages a line's whole open amount at its <i>earliest</i> receipt date (conservative — oldest),
///         not a per-receipt FIFO layering.</item>
///   <item>The uncovered-receipts sweep is scoped to <i>open</i> PO lines (the goods currently in GRNI); a
///         gap on an already-fully-billed line isn't itemized here, but it still shows in the GL-vs-operational
///         <c>Variance</c> (the primary, exhaustive control). A <i>reversed</i> accrual nets the GL to zero
///         yet leaves the receiving record covered (a JE exists), so it reads as a variance, not an uncovered
///         receipt — cross-reference the ledger to tell a reversal from a never-posted accrual.</item>
/// </list></para>
/// </summary>
public sealed class GrniReconciliationService(AppDbContext db, IClock clock) : IGrniReconciliationService
{
    private const string KeyGrni = "GRNI";
    private const string ReceiptSourceType = "Receipt";

    private static readonly (int From, int? To, string Label)[] BucketDefs =
    [
        (0, 30, "0-30"),
        (31, 60, "31-60"),
        (61, 90, "61-90"),
        (91, null, "91+"),
    ];

    public async Task<GrniReconciliation> GetGrniReconciliationAsync(
        int bookId, DateOnly? asOfDate = null, CancellationToken ct = default)
    {
        var asOf = asOfDate ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

        // Book rounding tolerance absorbs sub-cent GL-vs-operational residue (fractional qty × price posted at
        // currency scale vs computed fresh). Default to a cent if the book is missing one.
        var tolerance = await db.Books.AsNoTracking()
            .Where(b => b.Id == bookId)
            .Select(b => (decimal?)b.RoundingTolerance)
            .FirstOrDefaultAsync(ct) ?? 0.01m;

        var glBalance = await GlGrniBalanceAsync(bookId, asOf, ct);

        // ── Operational open GRNI: open PO lines (received > billed), at PO price ──
        var openLines = await
            (from pol in db.Set<PurchaseOrderLine>()
             join po in db.Set<PurchaseOrder>() on pol.PurchaseOrderId equals po.Id
             where pol.ReceivedQuantity > pol.BilledQuantity
             select new OpenLine
             {
                 LineId = pol.Id,
                 PurchaseOrderId = po.Id,
                 PoNumber = po.PONumber,
                 VendorId = po.VendorId,
                 OpenQuantity = pol.ReceivedQuantity - pol.BilledQuantity,
                 UnitPrice = pol.UnitPrice,
             })
            .ToListAsync(ct);

        var lineIds = openLines.Select(l => l.LineId).ToList();

        // Earliest receipt date per open line (ages the line's whole open amount — conservative).
        var earliestReceipt = await db.Set<ReceivingRecord>()
            .Where(r => lineIds.Contains(r.PurchaseOrderLineId))
            .GroupBy(r => r.PurchaseOrderLineId)
            .Select(g => new { LineId = g.Key, Earliest = g.Min(r => r.CreatedAt) })
            .ToDictionaryAsync(x => x.LineId, x => x.Earliest, ct);

        var vendorIds = openLines.Select(l => l.VendorId).Distinct().ToList();
        var vendorNames = await db.Set<Vendor>()
            .IgnoreQueryFilters()
            .Where(v => vendorIds.Contains(v.Id))
            .Select(v => new { v.Id, v.CompanyName })
            .ToDictionaryAsync(v => v.Id, v => v.CompanyName, ct);

        var poRows = new List<GrniPoRow>();
        var grandBucketTotals = new decimal[BucketDefs.Length];

        foreach (var poGroup in openLines.GroupBy(l => l.PurchaseOrderId))
        {
            var bucketAmounts = new decimal[BucketDefs.Length];
            foreach (var line in poGroup)
            {
                var openAmount = line.OpenQuantity * line.UnitPrice;
                if (openAmount == 0m)
                    continue; // open quantity at a zero PO price contributes no GRNI

                // A line is dated by its earliest receipt; a line received entirely after AsOf is excluded.
                var receiptDate = earliestReceipt.TryGetValue(line.LineId, out var dto)
                    ? DateOnly.FromDateTime(dto.UtcDateTime)
                    : asOf; // no receiving record found (shouldn't happen for a received line) → current bucket
                if (receiptDate > asOf)
                    continue;

                var idx = BucketIndexForAge(asOf.DayNumber - receiptDate.DayNumber);
                bucketAmounts[idx] += openAmount;
                grandBucketTotals[idx] += openAmount;
            }

            var openBalance = bucketAmounts.Sum();
            if (openBalance == 0m)
                continue;

            var first = poGroup.First();
            poRows.Add(new GrniPoRow
            {
                PurchaseOrderId = poGroup.Key,
                PoNumber = first.PoNumber,
                VendorId = first.VendorId,
                VendorName = vendorNames.TryGetValue(first.VendorId, out var n) ? n : $"Vendor {first.VendorId}",
                OpenAmount = openBalance,
                Buckets = BuildBuckets(bucketAmounts),
            });
        }

        var (uncovered, truncated) = await SweepUncoveredAsync(bookId, asOf, openLines, ct);

        return new GrniReconciliation
        {
            BookId = bookId,
            AsOfDate = asOf,
            RoundingTolerance = tolerance,
            GlBalance = glBalance,
            OperationalOpen = poRows.Sum(r => r.OpenAmount),
            PurchaseOrders = poRows.OrderByDescending(r => r.OpenAmount).ThenBy(r => r.PoNumber).ToList(),
            TotalsByBucket = BuildBuckets(grandBucketTotals),
            GrandTotal = poRows.Sum(r => r.OpenAmount),
            UncoveredReceipts = uncovered,
            UncoveredTruncated = truncated,
        };
    }

    private async Task<decimal> GlGrniBalanceAsync(int bookId, DateOnly asOf, CancellationToken ct)
    {
        var grniAccountIds = await db.AccountDeterminationRules
            .Where(r => r.BookId == bookId && r.Key == KeyGrni)
            .Select(r => r.GlAccountId)
            .ToListAsync(ct);

        if (grniAccountIds.Count == 0)
            return 0m;

        // Credit-positive (GRNI is a credit-normal liability): receipt accruals (Cr) raise it, bill clears
        // (Dr) lower it. Posted + Reversed both contribute (a reversal nets its original), matching the TB.
        return await
            (from line in db.JournalLines.IgnoreQueryFilters()
             join entry in db.JournalEntries.IgnoreQueryFilters() on line.JournalEntryId equals entry.Id
             where entry.BookId == bookId
                 && grniAccountIds.Contains(line.GlAccountId)
                 && (entry.Status == JournalEntryStatus.Posted || entry.Status == JournalEntryStatus.Reversed)
                 && entry.EntryDate <= asOf
             select line.Credit > 0 ? line.FunctionalAmount : -line.FunctionalAmount)
            .SumAsync(ct);
    }

    /// <summary>
    /// Line-level coverage: among the open PO lines' receiving records (on/before <c>AsOf</c>), find any with
    /// no GRNI accrual entry. Coverage is keyed by the receipt posting's idempotency key
    /// (<c>Inventory:Receipt:{poId}:{receiptNumber}:RECEIPT</c>) — line-level, not a coarse (SourceType,SourceId)
    /// presence check. A null receipt number is inherently un-postable (the receipt poster no-ops on it).
    /// </summary>
    private async Task<(IReadOnlyList<GrniUncoveredReceipt> Items, bool Truncated)> SweepUncoveredAsync(
        int bookId, DateOnly asOf, List<OpenLine> openLines, CancellationToken ct)
    {
        // Only lines with an actual open GRNI amount need coverage — a zero-priced receipt accrues no GRNI,
        // so its receiving record isn't "uncovered" (there is nothing to cover).
        var coveredLines = openLines.Where(l => l.UnitPrice > 0m).ToList();
        if (coveredLines.Count == 0)
            return ([], false);

        var lineToPo = coveredLines.ToDictionary(l => l.LineId, l => l.PurchaseOrderId);
        var lineIds = coveredLines.Select(l => l.LineId).ToList();

        var accrualKeys = await db.JournalEntries
            .IgnoreQueryFilters()
            .Where(e => e.BookId == bookId
                && e.Source == JournalSource.Inventory
                && e.SourceType == ReceiptSourceType
                && (e.Status == JournalEntryStatus.Posted || e.Status == JournalEntryStatus.Reversed)
                && e.IdempotencyKey != null)
            .Select(e => e.IdempotencyKey!)
            .ToListAsync(ct);
        var accrualKeySet = accrualKeys.ToHashSet(StringComparer.Ordinal);

        var records = await db.Set<ReceivingRecord>()
            .Where(r => lineIds.Contains(r.PurchaseOrderLineId) && r.QuantityReceived > 0)
            .Select(r => new { r.Id, r.PurchaseOrderLineId, r.ReceiptNumber, r.QuantityReceived, r.CreatedAt })
            .ToListAsync(ct);

        var uncovered = new List<GrniUncoveredReceipt>();
        var truncated = false;

        foreach (var r in records.OrderBy(r => r.CreatedAt))
        {
            var receivedDate = DateOnly.FromDateTime(r.CreatedAt.UtcDateTime);
            if (receivedDate > asOf)
                continue;

            var poId = lineToPo[r.PurchaseOrderLineId];

            string? reason = null;
            // Match the receipt poster, which no-ops on a null/blank receipt number (IsNullOrWhiteSpace) —
            // such a record is inherently un-postable, so the reason is NO_RECEIPT_NUMBER, not a missing accrual.
            if (string.IsNullOrWhiteSpace(r.ReceiptNumber))
                reason = "NO_RECEIPT_NUMBER";
            else if (!accrualKeySet.Contains($"{JournalSource.Inventory}:{ReceiptSourceType}:{poId}:{r.ReceiptNumber}:RECEIPT"))
                reason = "NO_ACCRUAL_POSTED";

            if (reason is null)
                continue;

            if (uncovered.Count >= GrniReconciliation.UncoveredReceiptLimit)
            {
                truncated = true;
                break;
            }

            uncovered.Add(new GrniUncoveredReceipt
            {
                ReceivingRecordId = r.Id,
                PurchaseOrderId = poId,
                PurchaseOrderLineId = r.PurchaseOrderLineId,
                ReceiptNumber = r.ReceiptNumber,
                QuantityReceived = r.QuantityReceived,
                ReceivedDate = receivedDate,
                Reason = reason,
            });
        }

        return (uncovered, truncated);
    }

    private static int BucketIndexForAge(int ageDays)
    {
        for (var i = 0; i < BucketDefs.Length; i++)
        {
            var (from, to, _) = BucketDefs[i];
            if (ageDays >= from && (to is null || ageDays <= to))
                return i;
        }
        return 0; // age < 0 is excluded upstream; clamp defensively
    }

    private static IReadOnlyList<GrniAgingBucket> BuildBuckets(decimal[] amounts)
        => BucketDefs
            .Select((d, i) => new GrniAgingBucket { FromDays = d.From, ToDays = d.To, Label = d.Label, Amount = amounts[i] })
            .ToList();

    private sealed class OpenLine
    {
        public int LineId { get; init; }
        public int PurchaseOrderId { get; init; }
        public string PoNumber { get; init; } = string.Empty;
        public int VendorId { get; init; }
        public decimal OpenQuantity { get; init; }
        public decimal UnitPrice { get; init; }
    }
}
