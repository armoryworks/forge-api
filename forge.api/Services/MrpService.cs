using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Services;

public class MrpService(
    AppDbContext db,
    IClock clock,
    IPartSourcingResolver sourcingResolver,
    ILogger<MrpService> logger) : IMrpService
{
    public async Task<MrpRunResponseModel> ExecuteRunAsync(MrpRunOptions options, CancellationToken cancellationToken = default)
    {
        // Concurrency guard — only one running MRP at a time
        var existingRun = await db.MrpRuns
            .AsNoTracking()
            .AnyAsync(r => r.Status == MrpRunStatus.Running, cancellationToken);

        if (existingRun)
            throw new InvalidOperationException("An MRP run is already in progress. Please wait for it to complete.");

        var runNumber = $"MRP-{clock.UtcNow:yyyyMMdd-HHmmss}";
        var mrpRun = new MrpRun
        {
            RunNumber = runNumber,
            RunType = options.RunType,
            Status = MrpRunStatus.Running,
            IsSimulation = options.IsSimulation,
            StartedAt = clock.UtcNow,
            PlanningHorizonDays = options.PlanningHorizonDays,
            InitiatedByUserId = options.InitiatedByUserId,
        };

        db.MrpRuns.Add(mrpRun);
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            var horizonEnd = clock.UtcNow.AddDays(options.PlanningHorizonDays);

            // Step 1: Gather MRP-planned parts
            var partsQuery = db.Parts.AsNoTracking()
                .Where(p => p.IsMrpPlanned && p.Status == PartStatus.Active);

            if (options.PartIds is { Count: > 0 })
                partsQuery = partsQuery.Where(p => options.PartIds.Contains(p.Id));

            var parts = await partsQuery
                .Select(p => new
                {
                    p.Id,
                    p.PartNumber,
                    p.Description,
                    p.LotSizingRule,
                    p.FixedOrderQuantity,
                    p.MinimumOrderQuantity,
                    p.OrderMultiple,
                    p.SafetyStockDays,
                })
                .ToListAsync(cancellationToken);

            if (parts.Count == 0)
            {
                mrpRun.Status = MrpRunStatus.Completed;
                mrpRun.CompletedAt = clock.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
                return MapToResponse(mrpRun);
            }

            var partIds = parts.Select(p => p.Id).ToHashSet();

            // Step 2: Gather independent demand (unfulfilled SO lines)
            var soDemand = await db.SalesOrderLines
                .AsNoTracking()
                .Include(l => l.SalesOrder)
                .Where(l => l.SalesOrder!.Status != SalesOrderStatus.Cancelled
                    && l.SalesOrder!.Status != SalesOrderStatus.Completed
                    && l.PartId.HasValue
                    && partIds.Contains(l.PartId!.Value)
                    && (l.Quantity - l.ShippedQuantity) > 0)
                .Select(l => new
                {
                    l.Id,
                    PartId = l.PartId!.Value,
                    Quantity = (decimal)(l.Quantity - l.ShippedQuantity),
                    RequiredDate = l.SalesOrder!.RequestedDeliveryDate ?? l.SalesOrder.ConfirmedDate ?? l.SalesOrder.CreatedAt.AddDays(30),
                })
                .ToListAsync(cancellationToken);

            // Step 2b: Gather MPS demand (active master schedule lines)
            var mpsDemand = await db.MasterScheduleLines
                .AsNoTracking()
                .Include(l => l.MasterSchedule)
                .Where(l => l.MasterSchedule!.Status == MasterScheduleStatus.Active
                    && l.DueDate <= horizonEnd
                    && partIds.Contains(l.PartId))
                .Select(l => new
                {
                    l.Id,
                    l.PartId,
                    l.Quantity,
                    RequiredDate = l.DueDate,
                })
                .ToListAsync(cancellationToken);

            // Step 3: Gather existing supply
            // 3a: On-hand inventory (BinContents aggregated by part)
            var onHandByPart = await db.BinContents
                .AsNoTracking()
                .Where(bc => bc.EntityType == "Part"
                    && bc.Status != BinContentStatus.QcHold
                    && partIds.Contains(bc.EntityId))
                .GroupBy(bc => bc.EntityId)
                .Select(g => new { PartId = g.Key, Quantity = g.Sum(bc => bc.Quantity - bc.ReservedQuantity) })
                .ToListAsync(cancellationToken);

            var onHandMap = onHandByPart.ToDictionary(x => x.PartId, x => x.Quantity);

            // 3b: Open PO lines
            var openPoLines = await db.PurchaseOrderLines
                .AsNoTracking()
                .Include(l => l.PurchaseOrder)
                .Where(l => l.PurchaseOrder!.Status != PurchaseOrderStatus.Cancelled
                    && l.PurchaseOrder!.Status != PurchaseOrderStatus.Closed
                    && partIds.Contains(l.PartId)
                    && (l.OrderedQuantity - l.ReceivedQuantity) > 0)
                .Select(l => new
                {
                    l.Id,
                    l.PartId,
                    Quantity = (decimal)(l.OrderedQuantity - l.ReceivedQuantity),
                    AvailableDate = l.PurchaseOrder!.ExpectedDeliveryDate != null
                        ? l.PurchaseOrder.ExpectedDeliveryDate.Value
                        : l.PurchaseOrder.SubmittedDate != null
                            ? l.PurchaseOrder.SubmittedDate.Value.AddDays(30)
                            : l.PurchaseOrder.CreatedAt.AddDays(30),
                })
                .ToListAsync(cancellationToken);

            // 3c: In-progress production runs
            var activeRuns = await db.ProductionRuns
                .AsNoTracking()
                .Where(r => (r.Status == ProductionRunStatus.Planned || r.Status == ProductionRunStatus.InProgress)
                    && partIds.Contains(r.PartId))
                .Select(r => new
                {
                    r.Id,
                    r.PartId,
                    Quantity = (decimal)(r.TargetQuantity - r.CompletedQuantity),
                    AvailableDate = r.CompletedAt ?? clock.UtcNow.AddDays(14),
                })
                .ToListAsync(cancellationToken);

            // 3d: Firmed planned orders from previous runs
            var firmedOrders = await db.MrpPlannedOrders
                .AsNoTracking()
                .Where(po => po.IsFirmed
                    && po.Status == MrpPlannedOrderStatus.Firmed
                    && partIds.Contains(po.PartId))
                .Select(po => new
                {
                    po.Id,
                    po.PartId,
                    po.Quantity,
                    AvailableDate = po.DueDate,
                })
                .ToListAsync(cancellationToken);

            // Step 4: Build BOM tree and compute low-level codes
            var bomEntries = await db.BOMEntries
                .AsNoTracking()
                .Where(b => partIds.Contains(b.ParentPartId) || partIds.Contains(b.ChildPartId))
                .ToListAsync(cancellationToken);

            // Include child parts that may not be in the original partIds
            var allPartIds = partIds.ToHashSet();
            foreach (var bom in bomEntries)
            {
                allPartIds.Add(bom.ChildPartId);
                allPartIds.Add(bom.ParentPartId);
            }

            var lowLevelCodes = ComputeLowLevelCodes(bomEntries, allPartIds);

            // Step 5: Process level by level (low-level code ascending)
            var demandRecords = new List<MrpDemand>();
            var supplyRecords = new List<MrpSupply>();
            var plannedOrders = new List<MrpPlannedOrder>();
            var exceptions = new List<MrpException>();

            // Add independent demand records
            foreach (var so in soDemand)
            {
                demandRecords.Add(new MrpDemand
                {
                    MrpRunId = mrpRun.Id,
                    PartId = so.PartId,
                    Source = MrpDemandSource.SalesOrder,
                    SourceEntityId = so.Id,
                    Quantity = so.Quantity,
                    RequiredDate = so.RequiredDate,
                    IsDependent = false,
                    BomLevel = 0,
                });
            }

            // Add MPS demand records
            foreach (var mps in mpsDemand)
            {
                demandRecords.Add(new MrpDemand
                {
                    MrpRunId = mrpRun.Id,
                    PartId = mps.PartId,
                    Source = MrpDemandSource.MasterSchedule,
                    SourceEntityId = mps.Id,
                    Quantity = mps.Quantity,
                    RequiredDate = mps.RequiredDate,
                    IsDependent = false,
                    BomLevel = 0,
                });
            }

            // Add supply records
            foreach (var oh in onHandByPart.Where(x => x.Quantity > 0))
            {
                supplyRecords.Add(new MrpSupply
                {
                    MrpRunId = mrpRun.Id,
                    PartId = oh.PartId,
                    Source = MrpSupplySource.OnHand,
                    Quantity = oh.Quantity,
                    AvailableDate = clock.UtcNow,
                });
            }

            foreach (var po in openPoLines)
            {
                supplyRecords.Add(new MrpSupply
                {
                    MrpRunId = mrpRun.Id,
                    PartId = po.PartId,
                    Source = MrpSupplySource.PurchaseOrder,
                    SourceEntityId = po.Id,
                    Quantity = po.Quantity,
                    AvailableDate = po.AvailableDate,
                });
            }

            foreach (var run in activeRuns)
            {
                supplyRecords.Add(new MrpSupply
                {
                    MrpRunId = mrpRun.Id,
                    PartId = run.PartId,
                    Source = MrpSupplySource.ProductionRun,
                    SourceEntityId = run.Id,
                    Quantity = run.Quantity,
                    AvailableDate = run.AvailableDate,
                });
            }

            foreach (var fo in firmedOrders)
            {
                supplyRecords.Add(new MrpSupply
                {
                    MrpRunId = mrpRun.Id,
                    PartId = fo.PartId,
                    Source = MrpSupplySource.PlannedOrder,
                    SourceEntityId = fo.Id,
                    Quantity = fo.Quantity,
                    AvailableDate = fo.AvailableDate,
                });
            }

            // Load all part info (including children discovered via BOM)
            var allParts = await db.Parts.AsNoTracking()
                .Where(p => allPartIds.Contains(p.Id))
                .Select(p => new
                {
                    p.Id,
                    p.PartNumber,
                    p.Description,
                    p.LotSizingRule,
                    p.FixedOrderQuantity,
                    p.MinimumOrderQuantity,
                    p.OrderMultiple,
                    p.SafetyStockDays,
                })
                .ToDictionaryAsync(p => p.Id, cancellationToken);

            // Pillar 3 — bulk-resolve effective sourcing values across the
            // entire BOM tree. Lead time for supply-window timing comes
            // from the preferred VendorPart row when configured, falling
            // back to the Part snapshot.
            var sourcingByPart = await sourcingResolver.ResolveManyAsync(allPartIds.ToList(), cancellationToken);

            // Also load on-hand for child parts
            var childOnHand = await db.BinContents
                .AsNoTracking()
                .Where(bc => bc.EntityType == "Part"
                    && bc.Status != BinContentStatus.QcHold
                    && allPartIds.Contains(bc.EntityId)
                    && !partIds.Contains(bc.EntityId))
                .GroupBy(bc => bc.EntityId)
                .Select(g => new { PartId = g.Key, Quantity = g.Sum(bc => bc.Quantity - bc.ReservedQuantity) })
                .ToListAsync(cancellationToken);

            foreach (var ch in childOnHand)
                onHandMap.TryAdd(ch.PartId, ch.Quantity);

            // Group BOM by parent
            var bomByParent = bomEntries
                .GroupBy(b => b.ParentPartId)
                .ToDictionary(g => g.Key, g => g.ToList());

            // Pre-group demands and supplies by part for O(1) lookup (mutable for BOM explosion)
            var demandsByPart = demandRecords
                .GroupBy(d => d.PartId)
                .ToDictionary(g => g.Key, g => g.ToList());
            var suppliesByPart = supplyRecords
                .Where(s => s.Source != MrpSupplySource.OnHand)
                .GroupBy(s => s.PartId)
                .ToDictionary(g => g.Key, g => g.ToList());

            // Process by level
            var sortedLevels = lowLevelCodes
                .GroupBy(kv => kv.Value)
                .OrderBy(g => g.Key);

            foreach (var levelGroup in sortedLevels)
            {
                foreach (var partId in levelGroup.Select(kv => kv.Key))
                {
                    // Gather all demand for this part at this level
                    if (!demandsByPart.TryGetValue(partId, out var partDemandList) || partDemandList.Count == 0)
                        continue;

                    var partDemands = partDemandList.OrderBy(d => d.RequiredDate).ToList();

                    // Calculate available supply
                    var availableOnHand = onHandMap.GetValueOrDefault(partId, 0);
                    var partSupplies = suppliesByPart.TryGetValue(partId, out var supplyList)
                        ? supplyList.OrderBy(s => s.AvailableDate).ToList()
                        : [];

                    var part = allParts.GetValueOrDefault(partId);
                    // Lead time comes from the preferred VendorPart row;
                    // default 14 days when no preferred VendorPart is set.
                    var resolvedLeadTime = sourcingByPart.TryGetValue(partId, out var sv)
                        ? sv.LeadTimeDays
                        : null;
                    var leadTime = resolvedLeadTime ?? 14;
                    var lotRule = part?.LotSizingRule ?? LotSizingRule.LotForLot;

                    var runningOnHand = availableOnHand;

                    foreach (var demand in partDemands)
                    {
                        // Net against scheduled receipts arriving before demand date
                        foreach (var supply in partSupplies.Where(s => s.AvailableDate <= demand.RequiredDate && s.AllocatedQuantity < s.Quantity))
                        {
                            var available = supply.Quantity - supply.AllocatedQuantity;
                            runningOnHand += available;
                            supply.AllocatedQuantity = supply.Quantity;
                        }

                        var netRequirement = demand.Quantity - runningOnHand;

                        if (netRequirement > 0)
                        {
                            // Lot size the order
                            var orderQty = LotSizer.Apply(
                                lotRule,
                                netRequirement,
                                part?.FixedOrderQuantity,
                                part?.MinimumOrderQuantity,
                                part?.OrderMultiple);

                            // Determine order type based on BOM
                            var orderType = bomByParent.ContainsKey(partId)
                                ? MrpOrderType.Manufacture
                                : MrpOrderType.Purchase;

                            // Lead-time offset
                            var dueDate = demand.RequiredDate;
                            var startDate = dueDate.AddDays(-leadTime);

                            if (startDate < clock.UtcNow)
                            {
                                exceptions.Add(new MrpException
                                {
                                    MrpRunId = mrpRun.Id,
                                    PartId = partId,
                                    ExceptionType = MrpExceptionType.PastDue,
                                    Message = $"Planned order for {part?.PartNumber ?? partId.ToString()} requires start date {startDate:MM/dd/yyyy} which is in the past.",
                                    SuggestedAction = "Expedite this order or adjust the due date.",
                                });

                                startDate = clock.UtcNow;
                            }

                            var plannedOrder = new MrpPlannedOrder
                            {
                                MrpRunId = mrpRun.Id,
                                PartId = partId,
                                OrderType = orderType,
                                Status = MrpPlannedOrderStatus.Planned,
                                Quantity = orderQty,
                                StartDate = startDate,
                                DueDate = dueDate,
                            };
                            plannedOrders.Add(plannedOrder);

                            runningOnHand += orderQty;
                            runningOnHand -= demand.Quantity;

                            // BOM explosion — generate dependent demand for child parts
                            if (orderType == MrpOrderType.Manufacture && bomByParent.TryGetValue(partId, out var children))
                            {
                                foreach (var child in children)
                                {
                                    var childQty = orderQty * child.Quantity;
                                    // BOM-level override wins, then preferred VendorPart, then default 14 days.
                                    var childResolvedLeadTime = sourcingByPart.TryGetValue(child.ChildPartId, out var csv)
                                        ? csv.LeadTimeDays
                                        : null;
                                    var childLeadTime = child.LeadTimeDays ?? childResolvedLeadTime ?? 14;
                                    var childRequiredDate = startDate;

                                    var childDemand = new MrpDemand
                                    {
                                        MrpRunId = mrpRun.Id,
                                        PartId = child.ChildPartId,
                                        Source = MrpDemandSource.DependentDemand,
                                        Quantity = childQty,
                                        RequiredDate = childRequiredDate,
                                        IsDependent = true,
                                        BomLevel = levelGroup.Key + 1,
                                    };
                                    demandRecords.Add(childDemand);

                                    // Also add to the grouped lookup so lower levels see it
                                    if (!demandsByPart.TryGetValue(child.ChildPartId, out var childDemandList))
                                    {
                                        childDemandList = [];
                                        demandsByPart[child.ChildPartId] = childDemandList;
                                    }
                                    childDemandList.Add(childDemand);
                                }
                            }
                        }
                        else
                        {
                            runningOnHand -= demand.Quantity;
                        }
                    }

                    // Check for over-supply
                    if (runningOnHand > 0)
                    {
                        var totalDemand = partDemands.Sum(d => d.Quantity);
                        if (totalDemand > 0 && runningOnHand > totalDemand * 2)
                        {
                            exceptions.Add(new MrpException
                            {
                                MrpRunId = mrpRun.Id,
                                PartId = partId,
                                ExceptionType = MrpExceptionType.OverSupply,
                                Message = $"Projected on-hand for {part?.PartNumber ?? partId.ToString()} is {runningOnHand:N0} which exceeds demand by more than 2x.",
                                SuggestedAction = "Consider deferring or cancelling open orders.",
                            });
                        }
                    }
                }
            }

            // Generate expedite/defer exceptions for existing POs
            foreach (var supply in supplyRecords.Where(s => s.Source == MrpSupplySource.PurchaseOrder))
            {
                var relatedDemand = demandsByPart.TryGetValue(supply.PartId, out var demList)
                    ? demList.OrderBy(d => d.RequiredDate).FirstOrDefault()
                    : null;

                if (relatedDemand == null) continue;

                if (supply.AvailableDate > relatedDemand.RequiredDate.AddDays(7))
                {
                    var part = allParts.GetValueOrDefault(supply.PartId);
                    exceptions.Add(new MrpException
                    {
                        MrpRunId = mrpRun.Id,
                        PartId = supply.PartId,
                        ExceptionType = MrpExceptionType.Expedite,
                        Message = $"PO for {part?.PartNumber ?? supply.PartId.ToString()} arrives {supply.AvailableDate:MM/dd/yyyy} but needed by {relatedDemand.RequiredDate:MM/dd/yyyy}.",
                        SuggestedAction = "Contact vendor to expedite delivery.",
                    });
                }
            }

            // Persist all records
            if (demandRecords.Count > 0) db.MrpDemands.AddRange(demandRecords);
            if (supplyRecords.Count > 0) db.MrpSupplies.AddRange(supplyRecords);
            if (plannedOrders.Count > 0) db.MrpPlannedOrders.AddRange(plannedOrders);
            if (exceptions.Count > 0) db.MrpExceptions.AddRange(exceptions);

            mrpRun.TotalDemandCount = demandRecords.Count;
            mrpRun.TotalSupplyCount = supplyRecords.Count;
            mrpRun.PlannedOrderCount = plannedOrders.Count;
            mrpRun.ExceptionCount = exceptions.Count;
            mrpRun.Status = MrpRunStatus.Completed;
            mrpRun.CompletedAt = clock.UtcNow;

            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("MRP run {RunNumber} completed: {DemandCount} demands, {SupplyCount} supplies, {PlannedCount} planned orders, {ExceptionCount} exceptions",
                runNumber, demandRecords.Count, supplyRecords.Count, plannedOrders.Count, exceptions.Count);

            return MapToResponse(mrpRun);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "MRP run {RunNumber} failed", mrpRun.RunNumber);

            mrpRun.Status = MrpRunStatus.Failed;
            mrpRun.ErrorMessage = ex.Message;
            mrpRun.CompletedAt = clock.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);

            throw;
        }
    }

    public async Task<MrpPartPlanResponseModel> GetPartPlanAsync(int mrpRunId, int partId, CancellationToken cancellationToken = default)
    {
        var part = await db.Parts.AsNoTracking()
            .Where(p => p.Id == partId)
            .Select(p => new { p.Id, p.PartNumber, p.Name, p.Description })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Part {partId} not found.");

        var run = await db.MrpRuns.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == mrpRunId, cancellationToken)
            ?? throw new KeyNotFoundException($"MRP run {mrpRunId} not found.");

        var demands = await db.MrpDemands.AsNoTracking()
            .Where(d => d.MrpRunId == mrpRunId && d.PartId == partId)
            .OrderBy(d => d.RequiredDate)
            .ToListAsync(cancellationToken);

        var supplies = await db.MrpSupplies.AsNoTracking()
            .Where(s => s.MrpRunId == mrpRunId && s.PartId == partId)
            .OrderBy(s => s.AvailableDate)
            .ToListAsync(cancellationToken);

        var plannedOrders = await db.MrpPlannedOrders.AsNoTracking()
            .Where(po => po.MrpRunId == mrpRunId && po.PartId == partId)
            .OrderBy(po => po.DueDate)
            .ToListAsync(cancellationToken);

        // Build weekly buckets across the planning horizon
        var buckets = new List<MrpTimeBucket>();
        var onHand = supplies.Where(s => s.Source == MrpSupplySource.OnHand).Sum(s => s.Quantity);
        var start = run.StartedAt ?? run.CreatedAt;
        var end = start.AddDays(run.PlanningHorizonDays);
        var current = start;

        while (current < end)
        {
            var weekEnd = current.AddDays(7);

            var grossReq = demands.Where(d => d.RequiredDate >= current && d.RequiredDate < weekEnd).Sum(d => d.Quantity);
            var scheduledReceipts = supplies.Where(s => s.Source != MrpSupplySource.OnHand && s.AvailableDate >= current && s.AvailableDate < weekEnd).Sum(s => s.Quantity);
            var poReceipts = plannedOrders.Where(po => po.DueDate >= current && po.DueDate < weekEnd).Sum(po => po.Quantity);
            var poReleases = plannedOrders.Where(po => po.StartDate >= current && po.StartDate < weekEnd).Sum(po => po.Quantity);

            onHand = onHand + scheduledReceipts + poReceipts - grossReq;
            var netReq = grossReq - scheduledReceipts - Math.Max(0, onHand + grossReq - scheduledReceipts - poReceipts);

            buckets.Add(new MrpTimeBucket(
                PeriodStart: current,
                PeriodEnd: weekEnd,
                GrossRequirements: grossReq,
                ScheduledReceipts: scheduledReceipts,
                PlannedOrderReceipts: poReceipts,
                ProjectedOnHand: Math.Max(0, onHand),
                NetRequirements: Math.Max(0, netReq),
                PlannedOrderReleases: poReleases
            ));

            current = weekEnd;
        }

        return new MrpPartPlanResponseModel(part.Id, part.PartNumber, part.Description ?? part.Name, buckets);
    }

    public async Task<List<MrpPeggingResponseModel>> GetPeggingAsync(int mrpRunId, int partId, CancellationToken cancellationToken = default)
    {
        var demands = await db.MrpDemands.AsNoTracking()
            .Include(d => d.Part)
            .Where(d => d.MrpRunId == mrpRunId && d.PartId == partId)
            .OrderBy(d => d.RequiredDate)
            .ToListAsync(cancellationToken);

        var supplies = await db.MrpSupplies.AsNoTracking()
            .Where(s => s.MrpRunId == mrpRunId && s.PartId == partId)
            .OrderBy(s => s.AvailableDate)
            .ToListAsync(cancellationToken);

        var plannedOrders = await db.MrpPlannedOrders.AsNoTracking()
            .Where(po => po.MrpRunId == mrpRunId && po.PartId == partId)
            .OrderBy(po => po.DueDate)
            .ToListAsync(cancellationToken);

        var supplyQueue = new Queue<MrpSupply>(supplies);
        var poQueue = new Queue<MrpPlannedOrder>(plannedOrders);

        var results = new List<MrpPeggingResponseModel>();

        foreach (var demand in demands)
        {
            MrpSupply? matchedSupply = null;
            MrpPlannedOrder? matchedPo = null;

            if (supplyQueue.Count > 0 && supplyQueue.Peek().AllocatedQuantity < supplyQueue.Peek().Quantity)
                matchedSupply = supplyQueue.Peek();
            else if (poQueue.Count > 0)
                matchedPo = poQueue.Dequeue();

            results.Add(new MrpPeggingResponseModel(
                DemandId: demand.Id,
                DemandSource: demand.Source,
                PartId: demand.PartId,
                PartNumber: demand.Part?.PartNumber ?? "",
                DemandQuantity: demand.Quantity,
                RequiredDate: demand.RequiredDate,
                SupplyId: matchedSupply?.Id,
                SupplySource: matchedSupply?.Source,
                SupplyQuantity: matchedSupply?.Quantity,
                SupplyDate: matchedSupply?.AvailableDate,
                PlannedOrderId: matchedPo?.Id,
                PlannedOrderQuantity: matchedPo?.Quantity
            ));
        }

        return results;
    }

    private static Dictionary<int, int> ComputeLowLevelCodes(List<BOMEntry> bomEntries, HashSet<int> allPartIds)
    {
        var codes = allPartIds.ToDictionary(id => id, _ => 0);
        var childrenByParent = bomEntries
            .GroupBy(b => b.ParentPartId)
            .ToDictionary(g => g.Key, g => g.Select(b => b.ChildPartId).ToList());

        bool changed;
        var iterations = 0;
        const int maxIterations = 100;

        do
        {
            changed = false;
            iterations++;

            foreach (var (parentId, children) in childrenByParent)
            {
                var parentLevel = codes.GetValueOrDefault(parentId, 0);
                foreach (var childId in children)
                {
                    var requiredLevel = parentLevel + 1;
                    if (codes.TryGetValue(childId, out var currentLevel) && currentLevel < requiredLevel)
                    {
                        codes[childId] = requiredLevel;
                        changed = true;
                    }
                }
            }
        } while (changed && iterations < maxIterations);

        return codes;
    }

    private static MrpRunResponseModel MapToResponse(MrpRun run) => new(
        Id: run.Id,
        RunNumber: run.RunNumber,
        RunType: run.RunType,
        Status: run.Status,
        IsSimulation: run.IsSimulation,
        StartedAt: run.StartedAt,
        CompletedAt: run.CompletedAt,
        PlanningHorizonDays: run.PlanningHorizonDays,
        TotalDemandCount: run.TotalDemandCount,
        TotalSupplyCount: run.TotalSupplyCount,
        PlannedOrderCount: run.PlannedOrderCount,
        ExceptionCount: run.ExceptionCount,
        ErrorMessage: run.ErrorMessage,
        InitiatedByUserId: run.InitiatedByUserId
    );
}
