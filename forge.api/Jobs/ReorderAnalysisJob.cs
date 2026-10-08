using System.Globalization;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;

using Forge.Api.Capabilities;
using Forge.Api.Features.Replenishment;
using Forge.Api.Services;

namespace Forge.Api.Jobs;

/// <summary>
/// Daily Hangfire job — analyzes part burn rates, creates buy or make reorder suggestions
/// for parts that fall below their reorder thresholds, gives the replenishment assignee a
/// follow-up task per suggestion, and notifies the assignee or purchasing users.
/// </summary>
public class ReorderAnalysisJob(
    AppDbContext db,
    IClock clock,
    IPartSourcingResolver sourcingResolver,
    ILogger<ReorderAnalysisJob> logger,
    ICapabilitySnapshotProvider capabilities)
{
    private const int ChunkSize = 500;
    private const int DefaultBuyLeadTimeDays = 14;

    private static readonly BinMovementReason[] ConsumptionReasons =
        [BinMovementReason.Pick, BinMovementReason.Ship];

    private static readonly PurchaseOrderStatus[] OpenPoStatuses =
    [
        PurchaseOrderStatus.Draft,
        PurchaseOrderStatus.Submitted,
        PurchaseOrderStatus.Acknowledged,
        PurchaseOrderStatus.PartiallyReceived,
    ];

    private sealed record Supply(decimal Quantity, DateTimeOffset? Earliest);

    private sealed record NewSuggestion(ReorderSuggestion Suggestion, Part Part, int LeadTimeDays, bool IsMake);

    public async Task RunAnalysisAsync(CancellationToken ct = default)
    {
        // ── Capability gate (self-gating job — the VarianceWatchdogJob pattern):
        // replenishment analysis is capability-owned; when the capability is off (services /
        // construction installs) the schedule still ticks but the job is a no-op,
        // so toggling the capability takes effect without a restart.
        if (!capabilities.IsEnabled("CAP-PLAN-SAFETYSTOCK"))
            return;

        var now = clock.UtcNow;
        var cutoff90 = now.AddDays(-90);
        var calendar = await ShopCalendar.LoadDefaultAsync(db, ct);
        var today = ShopCalendar.DateOf(now);

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

        var newSuggestions = new List<NewSuggestion>();
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
            var makePartIds = parts
                .Where(p => p.ProcurementSource == ProcurementSource.Make)
                .Select(p => p.Id)
                .ToList();

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
                .Where(l => l.PartId != null
                    && partIds.Contains(l.PartId.Value)
                    && l.PurchaseOrder.DeletedAt == null
                    && OpenPoStatuses.Contains(l.PurchaseOrder.Status))
                .Select(l => new
                {
                    PartId = l.PartId!.Value,
                    RemainingQty = (decimal)(l.OrderedQuantity - l.ReceivedQuantity),
                    l.PurchaseOrder.ExpectedDeliveryDate,
                })
                .ToListAsync(ct);

            var poSupplyMap = incomingRaw
                .GroupBy(x => x.PartId)
                .ToDictionary(
                    g => g.Key,
                    g => new Supply(g.Sum(x => x.RemainingQty), g.Min(x => x.ExpectedDeliveryDate)));

            var jobSupplyMap = (await ReplenishmentPlanning.LoadOpenJobSupplyAsync(db, makePartIds, ct))
                .ToDictionary(kv => kv.Key, kv => new Supply(kv.Value.Quantity, kv.Value.EarliestDue));

            var routingByPart = await ReplenishmentPlanning.LoadRoutingsAsync(db, makePartIds, ct);

            Supply SupplyFor(Part part)
            {
                var map = part.ProcurementSource == ProcurementSource.Make ? jobSupplyMap : poSupplyMap;
                return map.TryGetValue(part.Id, out var supply) ? supply : new Supply(0m, null);
            }

            int? BuyLeadTime(Part part) =>
                sourcingByPart.TryGetValue(part.Id, out var sv) ? sv.LeadTimeDays : null;

            (int LeadTimeDays, decimal Quantity) PlanMake(Part part, decimal burnRate) =>
                ReplenishmentPlanning.PlanMake(
                    part, routingByPart.TryGetValue(part.Id, out var ops) ? ops : [], burnRate, calendar, today);

            // Expire pending suggestions where stock has recovered (for this chunk's parts)
            var chunkPendingSuggestions = pendingSuggestions
                .Where(s => partIds.Contains(s.PartId))
                .ToList();

            var expiredIds = new List<int>();
            foreach (var pending in chunkPendingSuggestions)
            {
                var part = parts.FirstOrDefault(p => p.Id == pending.PartId);
                if (part == null) continue;

                var stock = stockMap.TryGetValue(pending.PartId, out var s) ? s : null;
                var available = (stock?.OnHand ?? 0m) - (stock?.Reserved ?? 0m);
                var incomingQty = SupplyFor(part).Quantity;

                var stillNeeded = part.ProcurementSource switch
                {
                    ProcurementSource.Phantom => false,
                    ProcurementSource.Make => NeedsReorder(
                        part, PlanMake(part, pending.BurnRateDailyAvg).LeadTimeDays,
                        available, incomingQty, pending.BurnRateDailyAvg),
                    _ => NeedsReorder(part, BuyLeadTime(part), available, incomingQty, pending.BurnRateDailyAvg),
                };

                if (!stillNeeded)
                {
                    pending.Status = ReorderSuggestionStatus.Expired;
                    expiredIds.Add(pending.Id);
                }
            }

            if (expiredIds.Count > 0)
            {
                await ReplenishmentAssignee.CloseTasksAsync(db, expiredIds, FollowUpStatus.Dismissed, now, ct);
                await db.SaveChangesAsync(ct);
                logger.LogInformation("[ReorderAnalysis] Expired {Count} resolved suggestion(s) in chunk {Chunk}",
                    expiredIds.Count, processedChunks + 1);
            }

            // Analyze each part for new suggestions
            foreach (var part in parts)
            {
                if (part.ProcurementSource == ProcurementSource.Phantom)
                    continue;

                if (existingPendingPartIds.Contains(part.Id))
                    continue;

                var isMake = part.ProcurementSource == ProcurementSource.Make;

                var stock = stockMap.TryGetValue(part.Id, out var s) ? s : null;
                var onHand = stock?.OnHand ?? 0m;
                var reserved = stock?.Reserved ?? 0m;
                var available = onHand - reserved;

                var supply = SupplyFor(part);
                var incomingQty = supply.Quantity;

                // O(1) lookup instead of O(n) filter per part
                var partMovements = movementsByPart.TryGetValue(part.Id, out var pm)
                    ? pm
                    : [];

                var burnRate30 = CalcBurnRate(partMovements.Select(m => (m.Quantity, m.MovedAt)).ToList(), now, 30);
                var burnRate60 = CalcBurnRate(partMovements.Select(m => (m.Quantity, m.MovedAt)).ToList(), now, 60);
                var burnRate90 = CalcBurnRate(partMovements.Select(m => (m.Quantity, m.MovedAt)).ToList(), now, 90);

                var bestBurnRate = burnRate90 ?? burnRate60 ?? burnRate30 ?? 0m;
                var windowDays = burnRate90.HasValue ? 90 : burnRate60.HasValue ? 60 : burnRate30.HasValue ? 30 : 0;

                int leadTimeDays;
                decimal suggestedQty;
                if (isMake)
                {
                    (leadTimeDays, suggestedQty) = PlanMake(part, bestBurnRate);
                    if (!NeedsReorder(part, leadTimeDays, available, incomingQty, bestBurnRate))
                        continue;
                }
                else
                {
                    var effectiveLeadTime = BuyLeadTime(part);
                    if (!NeedsReorder(part, effectiveLeadTime, available, incomingQty, bestBurnRate))
                        continue;
                    leadTimeDays = effectiveLeadTime ?? DefaultBuyLeadTimeDays;
                    suggestedQty = ReplenishmentPlanning.SuggestQuantity(part, bestBurnRate, leadTimeDays);
                }

                int? daysRemaining = null;
                DateTimeOffset? projectedStockout = null;

                if (bestBurnRate > 0)
                {
                    var effectiveStock = available + incomingQty;
                    var days = (double)(effectiveStock / bestBurnRate);
                    daysRemaining = (int)Math.Floor(days);
                    projectedStockout = now.AddDays(days);
                }

                newSuggestions.Add(new NewSuggestion(
                    new ReorderSuggestion
                    {
                        PartId = part.Id,
                        VendorId = isMake ? null : part.PreferredVendorId,
                        CurrentStock = onHand,
                        AvailableStock = available,
                        BurnRateDailyAvg = bestBurnRate,
                        BurnRateWindowDays = windowDays,
                        DaysOfStockRemaining = daysRemaining,
                        ProjectedStockoutDate = projectedStockout,
                        IncomingPoQuantity = incomingQty,
                        EarliestPoArrival = supply.Earliest,
                        SuggestedQuantity = suggestedQty,
                    },
                    part,
                    leadTimeDays,
                    isMake));
            }

            processedChunks++;
        }

        if (newSuggestions.Count == 0)
        {
            logger.LogInformation("[ReorderAnalysis] No new reorder suggestions needed");
            return;
        }

        db.ReorderSuggestions.AddRange(newSuggestions.Select(n => n.Suggestion));
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "[ReorderAnalysis] Created {Count} new reorder suggestion(s)", newSuggestions.Count);

        var assigneeId = await ReplenishmentAssignee.ResolveActiveAsync(db, ct);
        if (assigneeId is int taskOwnerId)
            await AddFollowUpTasksAsync(newSuggestions, taskOwnerId, now, ct);

        List<int> notifyUserIds = assigneeId is int onlyAssignee
            ? [onlyAssignee]
            : await db.UserRoles
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
                Message = $"{newSuggestions.Count} part(s) need replenishment. Review and approve purchase orders or work orders.",
                EntityType = "reorder_suggestions",
                UserId = userId,
            });
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "[ReorderAnalysis] Notified {Count} user(s)", notifyUserIds.Count);
    }

    private async Task AddFollowUpTasksAsync(
        List<NewSuggestion> created, int assigneeId, DateTimeOffset now, CancellationToken ct)
    {
        var suggestionIds = created.Select(n => n.Suggestion.Id).ToList();
        var alreadyTasked = (await db.FollowUpTasks
            .Where(t => t.SourceEntityType == ReplenishmentAssignee.TaskSourceEntityType
                && suggestionIds.Contains(t.SourceEntityId)
                && t.Status == FollowUpStatus.Open)
            .Select(t => t.SourceEntityId)
            .ToListAsync(ct))
            .ToHashSet();

        var today = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);

        foreach (var (suggestion, part, leadTimeDays, isMake) in created)
        {
            if (alreadyTasked.Contains(suggestion.Id))
                continue;

            var quantity = FormatQuantity(suggestion.SuggestedQuantity);
            var dueDate = suggestion.ProjectedStockoutDate is DateTimeOffset stockout
                ? stockout.AddDays(-leadTimeDays)
                : today;
            if (dueDate < today)
                dueDate = today;

            db.FollowUpTasks.Add(new FollowUpTask
            {
                Title = isMake
                    ? $"Make {quantity} x {part.PartNumber}"
                    : $"Order {quantity} x {part.PartNumber}",
                Description =
                    $"Available {FormatQuantity(suggestion.AvailableStock)}, " +
                    $"reorder point {(part.ReorderPoint is decimal rp ? FormatQuantity(rp) : "not set")}, " +
                    $"daily use {suggestion.BurnRateDailyAvg.ToString("0.##", CultureInfo.InvariantCulture)}, " +
                    $"lead time {leadTimeDays} day(s).",
                AssignedToUserId = assigneeId,
                DueDate = dueDate,
                SourceEntityType = ReplenishmentAssignee.TaskSourceEntityType,
                SourceEntityId = suggestion.Id,
                TriggerType = FollowUpTriggerType.ReorderSuggested,
            });
        }
    }

    private static string FormatQuantity(decimal value) =>
        value.ToString("0.####", CultureInfo.InvariantCulture);

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
            var coverDays = (effectiveLeadTimeDays ?? DefaultBuyLeadTimeDays) + (part.SafetyStockDays ?? 7);
            return (available + incomingQty) < burnRate * coverDays;
        }

        if (part.MinStockThreshold.HasValue)
            return available <= part.MinStockThreshold.Value;

        return false;
    }
}
