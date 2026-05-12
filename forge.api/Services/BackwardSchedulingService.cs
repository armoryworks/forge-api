using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Services;

public class BackwardSchedulingService(
    AppDbContext db,
    IClock clock,
    IPartSourcingResolver sourcingResolver) : IBackwardSchedulingService
{
    private const int ShippingBufferDays = 2;
    private const int QcBufferDays = 1;
    private const int DefaultProductionDays = 5;
    private const int DefaultLeadTimeDays = 7;
    private const int HoursPerDay = 8;

    public async Task<BackwardSchedule> CalculateSchedule(int salesOrderLineId, CancellationToken ct)
    {
        var soLine = await db.SalesOrderLines
            .Include(l => l.SalesOrder)
            .Include(l => l.Part)
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == salesOrderLineId, ct);

        if (soLine is null)
            throw new KeyNotFoundException($"SalesOrderLine {salesOrderLineId} not found");

        var deliveryDate = soLine.SalesOrder.RequestedDeliveryDate ?? clock.UtcNow.AddDays(30);
        var shipBy = deliveryDate.AddDays(-ShippingBufferDays);
        var qcCompleteBy = shipBy.AddDays(-QcBufferDays);

        var productionDays = await CalculateProductionDaysAsync(soLine.PartId, ct);
        var productionCompleteBy = qcCompleteBy;
        var productionStartBy = productionCompleteBy.AddDays(-productionDays);
        var materialsNeededBy = productionStartBy;

        var maxLeadTimeDays = await CalculateMaxLeadTimeDaysAsync(soLine.PartId, ct);
        var poOrderBy = materialsNeededBy.AddDays(-maxLeadTimeDays);

        return new BackwardSchedule(
            DeliveryDate: deliveryDate,
            ShipBy: shipBy,
            QcCompleteBy: qcCompleteBy,
            ProductionCompleteBy: productionCompleteBy,
            ProductionStartBy: productionStartBy,
            MaterialsNeededBy: materialsNeededBy,
            PoOrderBy: poOrderBy);
    }

    public async Task<List<ScheduleMilestone>> CalculateMilestonesAsync(int salesOrderLineId, CancellationToken ct = default)
    {
        var schedule = await CalculateSchedule(salesOrderLineId, ct);
        var now = clock.UtcNow;

        return
        [
            CreateMilestone(salesOrderLineId, "delivery", schedule.DeliveryDate, now),
            CreateMilestone(salesOrderLineId, "ship_by", schedule.ShipBy, now),
            CreateMilestone(salesOrderLineId, "qc_complete_by", schedule.QcCompleteBy, now),
            CreateMilestone(salesOrderLineId, "production_complete_by", schedule.ProductionCompleteBy, now),
            CreateMilestone(salesOrderLineId, "production_start_by", schedule.ProductionStartBy, now),
            CreateMilestone(salesOrderLineId, "materials_needed_by", schedule.MaterialsNeededBy, now),
            CreateMilestone(salesOrderLineId, "po_order_by", schedule.PoOrderBy, now),
        ];
    }

    private async Task<int> CalculateProductionDaysAsync(int? partId, CancellationToken ct)
    {
        if (!partId.HasValue)
            return DefaultProductionDays;

        var totalMinutes = await db.Operations
            .Where(o => o.PartId == partId.Value)
            .SumAsync(o => (o.EstimatedMinutes ?? 0) + (int)o.SetupMinutes, ct);

        if (totalMinutes <= 0)
            return DefaultProductionDays;

        var totalHours = totalMinutes / 60.0;
        var days = (int)Math.Ceiling(totalHours / HoursPerDay);
        return Math.Max(days, DefaultProductionDays);
    }

    private async Task<int> CalculateMaxLeadTimeDaysAsync(int? partId, CancellationToken ct)
    {
        if (!partId.HasValue)
            return DefaultLeadTimeDays;

        // Pull every Buy BOM entry — including rows with a null per-line
        // LeadTimeDays. The legacy implementation filtered the nulls out at
        // the SQL level which silently dropped any child part whose buy
        // lead-time was tracked on the part snapshot or the preferred
        // VendorPart row instead of the BOM line.
        var buyEntries = await db.BOMEntries
            .Where(b => b.ParentPartId == partId.Value && b.SourceType == BOMSourceType.Buy)
            .Select(b => new { b.ChildPartId, b.LeadTimeDays })
            .ToListAsync(ct);

        if (buyEntries.Count == 0)
            return DefaultLeadTimeDays;

        // Resolve the snapshot/vendor lead time for any rows whose per-line
        // LeadTimeDays is null. Bulk-resolve in a single round trip and look
        // up by child id in the loop.
        var fallbackPartIds = buyEntries
            .Where(e => !e.LeadTimeDays.HasValue)
            .Select(e => e.ChildPartId)
            .Distinct()
            .ToList();

        IReadOnlyDictionary<int, Core.Models.PartSourcingValues>? fallback = null;
        if (fallbackPartIds.Count > 0)
        {
            fallback = await sourcingResolver.ResolveManyAsync(fallbackPartIds, ct);
        }

        var max = 0;
        var sawAny = false;
        foreach (var entry in buyEntries)
        {
            int? lead = entry.LeadTimeDays
                ?? (fallback != null && fallback.TryGetValue(entry.ChildPartId, out var v)
                    ? v.LeadTimeDays
                    : null);

            if (!lead.HasValue) continue;
            sawAny = true;
            if (lead.Value > max) max = lead.Value;
        }

        return sawAny ? max : DefaultLeadTimeDays;
    }

    private static ScheduleMilestone CreateMilestone(int salesOrderLineId, string milestoneType, DateTimeOffset targetDate, DateTimeOffset now)
    {
        return new ScheduleMilestone
        {
            SalesOrderLineId = salesOrderLineId,
            MilestoneType = milestoneType,
            TargetDate = targetDate,
            ActualDate = null,
            Notes = now > targetDate ? "At Risk" : null,
        };
    }
}

public record BackwardSchedule(
    DateTimeOffset DeliveryDate,
    DateTimeOffset ShipBy,
    DateTimeOffset QcCompleteBy,
    DateTimeOffset ProductionCompleteBy,
    DateTimeOffset ProductionStartBy,
    DateTimeOffset MaterialsNeededBy,
    DateTimeOffset PoOrderBy);
