using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Jobs;

/// <summary>
/// Daily Hangfire job — analyzes raw material burn rates, creates reorder suggestions
/// for parts that fall below their reorder thresholds, and notifies purchasing users.
/// </summary>
public class ReorderAnalysisJob(
    AppDbContext db,
    IClock clock,
    IPartSourcingResolver sourcingResolver,
    ILogger<ReorderAnalysisJob> logger)
{
    private const int ChunkSize = 500;

    private static readonly BinMovementReason[] ConsumptionReasons =
        [BinMovementReason.Pick, BinMovementReason.Ship];

    private static readonly PurchaseOrderStatus[] OpenPoStatuses =
    [
        PurchaseOrderStatus.Draft,
        PurchaseOrderStatus.Submitted,
        PurchaseOrderStatus.Acknowledged,
        PurchaseOrderStatus.PartiallyReceived,
    ];

    public async Task RunAnalysisAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var cutoff90 = now.AddDays(-90);

        logger.LogInformation("[ReorderAnalysis] Starting daily reorder analysis at {Time}", now);

        // Process parts in chunks to avoid loading the entire table into memory
        var totalPartCount = await db.Parts
            .Where(p => p.DeletedAt == null)
            .CountAsync(ct);

        if (totalPartCount == 0)
        {
            logger.LogInformation("[ReorderAnalysis] No parts found — skipping");
            return;
        }

        // Expire suggestions where stock has recovered (bulk update)
        // Note: ExecuteUpdateAsync is not supported by EF Core InMemory provider used in tests,
        // so we use the tracked-entity approach here for compatibility.
        var pendingSuggestions = await db.ReorderSuggestions
            .Where(s => s.Status == ReorderSuggestionStatus.Pending)
            .ToListAsync(ct);

        // Existing pending suggestions — don't create duplicates
        var existingPendingPartIds = pendingSuggestions
            .Select(s => s.PartId)
            .ToHashSet();

        var newSuggestions = new List<ReorderSuggestion>();
        var processedChunks = 0;

        while (processedChunks * ChunkSize < totalPartCount)
        {
            ct.ThrowIfCancellationRequested();

            var parts = await db.Parts
                .Where(p => p.DeletedAt == null)
                .OrderBy(p => p.Id)
                .Skip(processedChunks * ChunkSize)
                .Take(ChunkSize)
                .ToListAsync(ct);

            if (parts.Count == 0)
                break;

            var partIds = parts.Select(p => p.Id).ToList();

            // Bulk-resolve effective sourcing values for this chunk. Reads
            // come from the preferred VendorPart row only — the Part-level
            // LeadTimeDays snapshot was dropped along with the OEM-on-
            // VendorPart move.
            var sourcingByPart = await sourcingResolver.ResolveManyAsync(partIds, ct);

            // Current stock per part (for this chunk)
            var stockByPart = await db.BinContents
                .Where(bc => bc.EntityType == "part"
                    && partIds.Contains(bc.EntityId)
                    && bc.RemovedAt == null)
                .GroupBy(bc => bc.EntityId)
                .Select(g => new
                {
                    PartId = g.Key,
                    OnHand = g.Sum(bc => bc.Quantity),
                    Reserved = g.Sum(bc => bc.ReservedQuantity),
                })
                .ToListAsync(ct);

            var stockMap = stockByPart.ToDictionary(s => s.PartId);

            // Consumption movements over last 90 days (for this chunk), pre-grouped by EntityId
            var movementsByPart = (await db.BinMovements
                .Where(m => m.EntityType == "part"
                    && partIds.Contains(m.EntityId)
                    && m.Reason != null
                    && ConsumptionReasons.Contains(m.Reason!.Value)
                    && m.MovedAt >= cutoff90)
                .Select(m => new { m.EntityId, m.Quantity, m.MovedAt })
                .ToListAsync(ct))
                .GroupBy(m => m.EntityId)
                .ToDictionary(g => g.Key, g => g.ToList());

            // Incoming PO quantities per part (for this chunk)
            var incomingRaw = await db.PurchaseOrderLines
                .Include(l => l.PurchaseOrder)
                .Where(l => partIds.Contains(l.PartId)
                    && l.PurchaseOrder.DeletedAt == null
                    && OpenPoStatuses.Contains(l.PurchaseOrder.Status))
                .Select(l => new
                {
                    l.PartId,
                    RemainingQty = (decimal)(l.OrderedQuantity - l.ReceivedQuantity),
                    l.PurchaseOrder.ExpectedDeliveryDate,
                })
                .ToListAsync(ct);

            var incomingMap = incomingRaw
                .GroupBy(x => x.PartId)
                .ToDictionary(
                    g => g.Key,
                    g => new
                    {
                        TotalQty = g.Sum(x => x.RemainingQty),
                        EarliestDate = g.Min(x => x.ExpectedDeliveryDate),
                    });

            // Expire pending suggestions where stock has recovered (for this chunk's parts)
            var chunkPendingSuggestions = pendingSuggestions
                .Where(s => partIds.Contains(s.PartId))
                .ToList();

            var expiredCount = 0;
            foreach (var pending in chunkPendingSuggestions)
            {
                var stock = stockMap.TryGetValue(pending.PartId, out var s) ? s : null;
                var available = (stock?.OnHand ?? 0m) - (stock?.Reserved ?? 0m);
                var incoming = incomingMap.TryGetValue(pending.PartId, out var inc) ? inc : null;
                var incomingQty = incoming?.TotalQty ?? 0m;

                var part = parts.FirstOrDefault(p => p.Id == pending.PartId);
                if (part == null) continue;

                var effectiveLeadTime = sourcingByPart.TryGetValue(part.Id, out var sv)
                    ? sv.LeadTimeDays
                    : null;

                if (!NeedsReorder(part, effectiveLeadTime, available, incomingQty, pending.BurnRateDailyAvg))
                {
                    pending.Status = ReorderSuggestionStatus.Expired;
                    expiredCount++;
                }
            }

            if (expiredCount > 0)
            {
                await db.SaveChangesAsync(ct);
                logger.LogInformation("[ReorderAnalysis] Expired {Count} resolved suggestion(s) in chunk {Chunk}",
                    expiredCount, processedChunks + 1);
            }

            // Analyze each part for new suggestions
            foreach (var part in parts)
            {
                if (existingPendingPartIds.Contains(part.Id))
                    continue;

                var stock = stockMap.TryGetValue(part.Id, out var s) ? s : null;
                var onHand = stock?.OnHand ?? 0m;
                var reserved = stock?.Reserved ?? 0m;
                var available = onHand - reserved;

                var incoming = incomingMap.TryGetValue(part.Id, out var inc) ? inc : null;
                var incomingQty = incoming?.TotalQty ?? 0m;
                var earliestArrival = incoming?.EarliestDate;

                // O(1) lookup instead of O(n) filter per part
                var partMovements = movementsByPart.TryGetValue(part.Id, out var pm)
                    ? pm
                    : [];

                var burnRate30 = CalcBurnRate(partMovements.Select(m => (m.Quantity, m.MovedAt)).ToList(), now, 30);
                var burnRate60 = CalcBurnRate(partMovements.Select(m => (m.Quantity, m.MovedAt)).ToList(), now, 60);
                var burnRate90 = CalcBurnRate(partMovements.Select(m => (m.Quantity, m.MovedAt)).ToList(), now, 90);

                var bestBurnRate = burnRate90 ?? burnRate60 ?? burnRate30 ?? 0m;
                var windowDays = burnRate90.HasValue ? 90 : burnRate60.HasValue ? 60 : burnRate30.HasValue ? 30 : 0;

                var effectiveLeadTime = sourcingByPart.TryGetValue(part.Id, out var sv)
                    ? sv.LeadTimeDays
                    : null;

                if (!NeedsReorder(part, effectiveLeadTime, available, incomingQty, bestBurnRate))
                    continue;

                int? daysRemaining = null;
                DateTimeOffset? projectedStockout = null;

                if (bestBurnRate > 0)
                {
                    var effectiveStock = available + incomingQty;
                    var days = (double)(effectiveStock / bestBurnRate);
                    daysRemaining = (int)Math.Floor(days);
                    projectedStockout = now.AddDays(days);
                }

                // Suggested quantity: cover lead time + safety stock + reorder qty preference
                decimal suggestedQty;
                if (part.ReorderQuantity.HasValue && part.ReorderQuantity.Value > 0)
                {
                    suggestedQty = part.ReorderQuantity.Value;
                }
                else if (bestBurnRate > 0)
                {
                    var coverDays = (effectiveLeadTime ?? 14) + (part.SafetyStockDays ?? 14);
                    suggestedQty = bestBurnRate * coverDays;
                }
                else
                {
                    // No burn rate — suggest minimum threshold amount
                    suggestedQty = part.MinStockThreshold ?? 10m;
                }

                newSuggestions.Add(new ReorderSuggestion
                {
                    PartId = part.Id,
                    VendorId = part.PreferredVendorId,
                    CurrentStock = onHand,
                    AvailableStock = available,
                    BurnRateDailyAvg = bestBurnRate,
                    BurnRateWindowDays = windowDays,
                    DaysOfStockRemaining = daysRemaining,
                    ProjectedStockoutDate = projectedStockout,
                    IncomingPoQuantity = incomingQty,
                    EarliestPoArrival = earliestArrival,
                    SuggestedQuantity = Math.Ceiling(suggestedQty),
                });
            }

            processedChunks++;
        }

        if (newSuggestions.Count == 0)
        {
            logger.LogInformation("[ReorderAnalysis] No new reorder suggestions needed");
            return;
        }

        db.ReorderSuggestions.AddRange(newSuggestions);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "[ReorderAnalysis] Created {Count} new reorder suggestion(s)", newSuggestions.Count);

        // Notify purchasing users (Admin + Manager roles)
        var notifyUserIds = await db.UserRoles
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
            .Where(x => x.Name == "Admin" || x.Name == "Manager")
            .Select(x => x.UserId)
            .Distinct()
            .ToListAsync(ct);

        foreach (var userId in notifyUserIds)
        {
            db.Set<Notification>().Add(new Notification
            {
                Type = "reorder_suggestions_ready",
                Severity = "warning",
                Source = "inventory",
                Title = "Reorder Suggestions Ready",
                Message = $"{newSuggestions.Count} part(s) need replenishment. Review and approve purchase orders.",
                EntityType = "reorder_suggestions",
                UserId = userId,
            });
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "[ReorderAnalysis] Notified {Count} user(s)", notifyUserIds.Count);
    }

    private static decimal? CalcBurnRate(
        List<(decimal Quantity, DateTimeOffset MovedAt)> movements, DateTimeOffset now, int windowDays)
    {
        var cutoff = now.AddDays(-windowDays);
        var total = movements
            .Where(m => m.MovedAt >= cutoff)
            .Sum(m => m.Quantity);

        return total > 0 ? total / windowDays : null;
    }

    private static bool NeedsReorder(
        Part part, int? effectiveLeadTimeDays, decimal available, decimal incomingQty, decimal burnRate)
    {
        if (part.ReorderPoint.HasValue)
            return (available + incomingQty) <= part.ReorderPoint.Value;

        if (burnRate > 0)
        {
            var coverDays = (effectiveLeadTimeDays ?? 14) + (part.SafetyStockDays ?? 7);
            return (available + incomingQty) < burnRate * coverDays;
        }

        if (part.MinStockThreshold.HasValue)
            return available <= part.MinStockThreshold.Value;

        return false;
    }
}
