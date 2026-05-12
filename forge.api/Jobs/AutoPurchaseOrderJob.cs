using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.AutoPo;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Jobs;

/// <summary>
/// Daily Hangfire job — analyzes demand from confirmed sales orders, calculates material
/// shortfalls via BOM explosion, and creates auto-PO suggestions or draft/confirmed POs.
/// </summary>
public class AutoPurchaseOrderJob(
    AppDbContext db,
    IClock clock,
    ISystemSettingRepository settingsRepo,
    PurchaseOrderGenerator poGenerator,
    IPartSourcingResolver sourcingResolver,
    ILogger<AutoPurchaseOrderJob> logger)
{
    private static readonly PurchaseOrderStatus[] OpenPoStatuses =
    [
        PurchaseOrderStatus.Draft,
        PurchaseOrderStatus.Submitted,
        PurchaseOrderStatus.Acknowledged,
        PurchaseOrderStatus.PartiallyReceived,
    ];

    private static readonly SalesOrderStatus[] ActiveSoStatuses =
    [
        SalesOrderStatus.Confirmed,
        SalesOrderStatus.InProduction,
        SalesOrderStatus.PartiallyShipped,
    ];

    public async Task Execute(CancellationToken ct)
    {
        var now = clock.UtcNow;
        logger.LogInformation("[AutoPO] Starting auto-PO analysis at {Time}", now);

        // 1. Check master kill switch
        var enabledSetting = await settingsRepo.FindByKeyAsync("inventory:auto_po_enabled", ct);
        if (enabledSetting is null || !bool.TryParse(enabledSetting.Value, out var enabled) || !enabled)
        {
            logger.LogInformation("[AutoPO] Auto-PO is disabled — skipping");
            return;
        }

        // 2. Read global settings
        var modeSetting = await settingsRepo.FindByKeyAsync("inventory:auto_po_mode", ct);
        var globalMode = Enum.TryParse<AutoPoMode>(modeSetting?.Value, out var parsedMode)
            ? parsedMode
            : AutoPoMode.Draft;

        var bufferSetting = await settingsRepo.FindByKeyAsync("inventory:auto_po_buffer_days", ct);
        var bufferDays = bufferSetting is not null && int.TryParse(bufferSetting.Value, out var bd) ? bd : 3;

        // 3. Get all active SO lines with unshipped quantities and their parts
        var activeSOLines = await db.SalesOrderLines
            .AsNoTracking()
            .Include(l => l.SalesOrder)
            .Include(l => l.Part)
            .Where(l => l.PartId != null
                && l.SalesOrder.DeletedAt == null
                && ActiveSoStatuses.Contains(l.SalesOrder.Status)
                && l.Quantity > l.ShippedQuantity)
            .ToListAsync(ct);

        if (activeSOLines.Count == 0)
        {
            logger.LogInformation("[AutoPO] No active SO lines with unshipped quantities — skipping");
            return;
        }

        // 4. Get all Buy BOM entries for parts on active SOs
        var soPartIds = activeSOLines
            .Where(l => l.PartId.HasValue)
            .Select(l => l.PartId!.Value)
            .Distinct()
            .ToList();

        var buyBomEntries = await db.BOMEntries
            .AsNoTracking()
            .Include(b => b.ChildPart)
            .Where(b => soPartIds.Contains(b.ParentPartId)
                && b.SourceType == BOMSourceType.Buy
                && b.ChildPart.DeletedAt == null
                && !b.ChildPart.ExcludeFromAutoPo)
            .ToListAsync(ct);

        if (buyBomEntries.Count == 0)
        {
            logger.LogInformation("[AutoPO] No Buy BOM entries found for active SO parts — skipping");
            return;
        }

        // Build lookup: parentPartId -> list of Buy BOM entries
        var bomByParent = buyBomEntries
            .GroupBy(b => b.ParentPartId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Pillar 3 — bulk-resolve effective sourcing values for every child
        // part referenced by these Buy BOM entries. Reads come from the
        // preferred VendorPart row (Part snapshot columns were dropped post-
        // OEM-on-VendorPart move; vendor-specific terms only live there now).
        var bomChildPartIds = buyBomEntries.Select(b => b.ChildPartId).Distinct().ToList();
        var sourcingByChildPart = await sourcingResolver.ResolveManyAsync(bomChildPartIds, ct);

        // 5. Calculate demand per child part
        // demand = SUM(SO_line_remaining_qty * BOM_qty_per_unit) across all active SOs
        var demandByChildPart = new Dictionary<int, DemandInfo>();

        foreach (var soLine in activeSOLines)
        {
            if (!soLine.PartId.HasValue || !bomByParent.TryGetValue(soLine.PartId.Value, out var bomEntries))
                continue;

            var remainingQty = soLine.Quantity - soLine.ShippedQuantity;
            var deliveryDate = soLine.SalesOrder.RequestedDeliveryDate;

            foreach (var bom in bomEntries)
            {
                var demand = remainingQty * bom.Quantity;

                if (!demandByChildPart.TryGetValue(bom.ChildPartId, out var info))
                {
                    info = new DemandInfo
                    {
                        ChildPart = bom.ChildPart,
                        BomEntry = bom,
                    };
                    demandByChildPart[bom.ChildPartId] = info;
                }

                info.TotalDemand += demand;
                info.SourceSalesOrderIds.Add(soLine.SalesOrderId);

                if (deliveryDate.HasValue)
                {
                    // BOM-level override wins; otherwise the preferred
                    // VendorPart row's lead time; otherwise default 14 days.
                    var resolvedLeadTime = sourcingByChildPart.TryGetValue(bom.ChildPartId, out var resolved)
                        ? resolved.LeadTimeDays
                        : null;
                    var leadTimeDays = bom.LeadTimeDays ?? resolvedLeadTime ?? 14;
                    var neededBy = deliveryDate.Value.AddDays(-leadTimeDays - bufferDays);
                    if (info.EarliestNeededBy is null || neededBy < info.EarliestNeededBy)
                        info.EarliestNeededBy = neededBy;
                }
            }
        }

        if (demandByChildPart.Count == 0)
        {
            logger.LogInformation("[AutoPO] No material demand calculated — skipping");
            return;
        }

        // 6. Get current stock for demanded parts
        var childPartIds = demandByChildPart.Keys.ToList();

        var stockByPart = await db.BinContents
            .AsNoTracking()
            .Where(bc => bc.EntityType == "part"
                && childPartIds.Contains(bc.EntityId)
                && bc.RemovedAt == null)
            .GroupBy(bc => bc.EntityId)
            .Select(g => new { PartId = g.Key, OnHand = g.Sum(bc => bc.Quantity) })
            .ToDictionaryAsync(x => x.PartId, x => x.OnHand, ct);

        // 7. Get in-transit stock from open POs
        var inTransitByPart = await db.PurchaseOrderLines
            .AsNoTracking()
            .Include(l => l.PurchaseOrder)
            .Where(l => childPartIds.Contains(l.PartId)
                && l.PurchaseOrder.DeletedAt == null
                && OpenPoStatuses.Contains(l.PurchaseOrder.Status))
            .GroupBy(l => l.PartId)
            .Select(g => new { PartId = g.Key, InTransit = g.Sum(l => l.OrderedQuantity - l.ReceivedQuantity) })
            .ToDictionaryAsync(x => x.PartId, x => x.InTransit, ct);

        // 8. Check for existing pending suggestions to avoid duplicates
        var existingPendingPartIds = await db.AutoPoSuggestions
            .AsNoTracking()
            .Where(s => s.Status == AutoPoSuggestionStatus.Pending && childPartIds.Contains(s.PartId))
            .Select(s => s.PartId)
            .Distinct()
            .ToListAsync(ct);

        var existingPendingSet = existingPendingPartIds.ToHashSet();

        // 9. Load preferred vendors for parts that need them
        var partsNeedingVendor = demandByChildPart.Values
            .Select(d => d.ChildPart)
            .Where(p => p.PreferredVendorId.HasValue)
            .Select(p => p.PreferredVendorId!.Value)
            .Distinct()
            .ToList();

        // Also include vendor IDs from BOM entries
        var bomVendorIds = demandByChildPart.Values
            .Where(d => d.BomEntry.VendorId.HasValue)
            .Select(d => d.BomEntry.VendorId!.Value)
            .Distinct()
            .ToList();

        var allVendorIds = partsNeedingVendor.Union(bomVendorIds).Distinct().ToList();

        var vendorMap = allVendorIds.Count > 0
            ? await db.Vendors
                .AsNoTracking()
                .Where(v => allVendorIds.Contains(v.Id) && v.DeletedAt == null)
                .ToDictionaryAsync(v => v.Id, ct)
            : new Dictionary<int, Vendor>();

        // 10. Process each demanded part
        var suggestionsToCreate = new List<AutoPoSuggestion>();
        var posToCreate = new List<PoLineItem>();

        foreach (var (childPartId, demand) in demandByChildPart)
        {
            if (existingPendingSet.Contains(childPartId))
            {
                logger.LogDebug("[AutoPO] Skipping part {PartId} — pending suggestion already exists", childPartId);
                continue;
            }

            var part = demand.ChildPart;
            var currentStock = stockByPart.TryGetValue(childPartId, out var stock) ? stock : 0m;
            var inTransit = inTransitByPart.TryGetValue(childPartId, out var transit) ? transit : 0;

            // Calculate shortfall
            var shortfall = demand.TotalDemand - currentStock - inTransit + part.SafetyStockQty;
            if (shortfall <= 0)
            {
                logger.LogDebug("[AutoPO] Part {PartId} ({PartNumber}) has sufficient stock — skipping",
                    childPartId, part.PartNumber);
                continue;
            }

            // Determine order quantity with rounding
            var orderQty = (int)Math.Ceiling(shortfall);

            // PackSize / MinOrderQty come from the preferred VendorPart row.
            // Null = no per-vendor override configured; PO sizing falls back
            // to the raw shortfall quantity without rounding.
            var sourcing = sourcingByChildPart.TryGetValue(childPartId, out var partSourcing)
                ? partSourcing
                : null;
            var effectivePackSize = sourcing?.PackSize;
            var effectiveMinOrderQty = sourcing?.MinOrderQty;

            // Round up to pack size
            if (effectivePackSize is > 0)
                orderQty = (int)(Math.Ceiling((decimal)orderQty / effectivePackSize.Value) * effectivePackSize.Value);

            // Ensure minimum order quantity
            if (effectiveMinOrderQty is > 0 && orderQty < effectiveMinOrderQty.Value)
                orderQty = (int)Math.Ceiling(effectiveMinOrderQty.Value);

            // Determine vendor (prefer BOM entry vendor, then part preferred vendor)
            var vendorId = demand.BomEntry.VendorId ?? part.PreferredVendorId;
            if (!vendorId.HasValue || !vendorMap.ContainsKey(vendorId.Value))
            {
                logger.LogWarning("[AutoPO] Part {PartId} ({PartNumber}) has no valid vendor — skipping",
                    childPartId, part.PartNumber);
                continue;
            }

            var vendor = vendorMap[vendorId.Value];

            // Ensure minimum order amount for vendor
            if (vendor.MinOrderAmount.HasValue && vendor.MinOrderAmount.Value > 0)
            {
                // We don't have unit price here, so this check is deferred
                // The purchasing team will review the PO amounts
            }

            // Determine mode: vendor-specific overrides global
            var mode = vendor.AutoPoMode ?? globalMode;

            // Needed-by date: default to 14 days from now if no SO delivery date
            var neededBy = demand.EarliestNeededBy ?? now.AddDays(14);

            var soIds = demand.SourceSalesOrderIds.Distinct().ToList();

            posToCreate.Add(new PoLineItem(vendorId.Value, mode, childPartId, part.Description ?? part.Name, orderQty, neededBy, soIds));
        }

        if (posToCreate.Count == 0)
        {
            logger.LogInformation("[AutoPO] No shortfalls detected — no POs or suggestions created");
            return;
        }

        // 11. Group by vendor and batch items with needed-by dates within 7 days
        var groupedByVendor = posToCreate.GroupBy(p => p.VendorId);
        var createdPOs = new List<PurchaseOrder>();
        var createdSuggestionCount = 0;

        foreach (var vendorGroup in groupedByVendor)
        {
            var items = vendorGroup.OrderBy(p => p.NeededBy).ToList();
            var mode = items.First().Mode;

            if (mode == AutoPoMode.Suggest)
            {
                // Create individual suggestions
                foreach (var item in items)
                {
                    suggestionsToCreate.Add(new AutoPoSuggestion
                    {
                        PartId = item.PartId,
                        VendorId = item.VendorId,
                        SuggestedQty = item.Qty,
                        NeededByDate = item.NeededBy,
                        SourceSalesOrderIds = JsonSerializer.Serialize(item.SoIds),
                        Status = AutoPoSuggestionStatus.Pending,
                    });
                }
            }
            else
            {
                // Batch items with needed-by dates within 7 days into single POs
                var batches = BatchByNeededByDate(items, TimeSpan.FromDays(7));

                foreach (var batch in batches)
                {
                    var poStatus = mode == AutoPoMode.Automatic
                        ? PurchaseOrderStatus.Submitted
                        : PurchaseOrderStatus.Draft;

                    var allSoIds = batch.SelectMany(b => b.SoIds).Distinct().ToList();
                    var lines = batch.Select(b =>
                        (b.PartId, b.Description, b.Qty, 0m, b.NeededBy)).ToList();

                    var po = await poGenerator.GeneratePurchaseOrder(
                        vendorGroup.Key, lines, poStatus,
                        $"Auto-generated from demand analysis. Source SOs: {JsonSerializer.Serialize(allSoIds)}",
                        ct);

                    createdPOs.Add(po);
                }
            }
        }

        // Save suggestions
        if (suggestionsToCreate.Count > 0)
        {
            db.AutoPoSuggestions.AddRange(suggestionsToCreate);
            await db.SaveChangesAsync(ct);
            createdSuggestionCount = suggestionsToCreate.Count;
        }

        logger.LogInformation(
            "[AutoPO] Completed — created {SuggestionCount} suggestion(s) and {PoCount} PO(s)",
            createdSuggestionCount, createdPOs.Count);

        // 12. Notify purchasing users
        if (createdSuggestionCount > 0 || createdPOs.Count > 0)
        {
            var notifyUserIds = await db.UserRoles
                .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
                .Where(x => x.Name == "Admin" || x.Name == "Manager" || x.Name == "OfficeManager")
                .Select(x => x.UserId)
                .Distinct()
                .ToListAsync(ct);

            var message = BuildNotificationMessage(createdSuggestionCount, createdPOs.Count);

            foreach (var userId in notifyUserIds)
            {
                db.Notifications.Add(new Notification
                {
                    UserId = userId,
                    Type = "auto_po_generated",
                    Severity = "info",
                    Source = "inventory",
                    Title = "Auto-PO Analysis Complete",
                    Message = message,
                    EntityType = "auto_po",
                });
            }

            await db.SaveChangesAsync(ct);
            logger.LogInformation("[AutoPO] Notified {Count} user(s)", notifyUserIds.Count);
        }
    }

    private static string BuildNotificationMessage(int suggestions, int pos)
    {
        var parts = new List<string>();
        if (suggestions > 0) parts.Add($"{suggestions} suggestion(s) for review");
        if (pos > 0) parts.Add($"{pos} purchase order(s) created");
        return string.Join(" and ", parts) + ". Review in Inventory > Auto-PO.";
    }

    private static List<List<PoLineItem>> BatchByNeededByDate(List<PoLineItem> items, TimeSpan window)
    {
        var batches = new List<List<PoLineItem>>();
        if (items.Count == 0) return batches;

        var currentBatch = new List<PoLineItem> { items[0] };
        var batchStart = items[0].NeededBy;

        for (var i = 1; i < items.Count; i++)
        {
            if (items[i].NeededBy - batchStart <= window)
            {
                currentBatch.Add(items[i]);
            }
            else
            {
                batches.Add(currentBatch);
                currentBatch = [items[i]];
                batchStart = items[i].NeededBy;
            }
        }

        batches.Add(currentBatch);
        return batches;
    }

    private sealed record PoLineItem(int VendorId, AutoPoMode Mode, int PartId, string Description, int Qty, DateTimeOffset NeededBy, List<int> SoIds);

    private sealed class DemandInfo
    {
        public Part ChildPart { get; set; } = null!;
        public BOMEntry BomEntry { get; set; } = null!;
        public decimal TotalDemand { get; set; }
        public DateTimeOffset? EarliestNeededBy { get; set; }
        public HashSet<int> SourceSalesOrderIds { get; } = [];
    }
}
