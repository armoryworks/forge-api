using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Replenishment;

public record GetBurnRatesQuery(string? Search, bool NeedsReorderOnly) : IRequest<List<BurnRateResponseModel>>;

public class GetBurnRatesHandler(AppDbContext db, IPartSourcingResolver sourcingResolver)
    : IRequestHandler<GetBurnRatesQuery, List<BurnRateResponseModel>>
{
    private static readonly BinMovementReason[] ConsumptionReasons =
        [BinMovementReason.Pick, BinMovementReason.Ship];

    private record MovementRecord(int EntityId, decimal Quantity, DateTimeOffset MovedAt);

    public async Task<List<BurnRateResponseModel>> Handle(
        GetBurnRatesQuery request, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var cutoff90 = now.AddDays(-90);

        var partsQuery = db.Parts
            .Include(p => p.PreferredVendor)
            .Where(p => p.DeletedAt == null);

        if (!string.IsNullOrWhiteSpace(request.Search))
            partsQuery = partsQuery.Where(p =>
                p.PartNumber.Contains(request.Search) ||
                (p.Description != null && p.Description.Contains(request.Search)));

        var parts = await partsQuery
            .OrderBy(p => p.PartNumber)
            .ToListAsync(cancellationToken);

        var partIds = parts.Select(p => p.Id).ToList();

        // Pillar 3 — resolve effective sourcing values for every part in the
        // result set. The needs-reorder decision uses the resolved lead time.
        var sourcingByPart = await sourcingResolver.ResolveManyAsync(partIds, cancellationToken);

        // On-hand stock per part
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
            .ToListAsync(cancellationToken);

        var stockMap = stockByPart.ToDictionary(s => s.PartId);

        // Consumption movements over last 90 days
        var rawMovements = await db.BinMovements
            .Where(m => m.EntityType == "part"
                && partIds.Contains(m.EntityId)
                && m.Reason != null
                && ConsumptionReasons.Contains(m.Reason!.Value)
                && m.MovedAt >= cutoff90)
            .Select(m => new MovementRecord(m.EntityId, m.Quantity, m.MovedAt))
            .ToListAsync(cancellationToken);

        // Incoming open PO quantities
        var openStatuses = new[]
        {
            PurchaseOrderStatus.Draft,
            PurchaseOrderStatus.Submitted,
            PurchaseOrderStatus.Acknowledged,
            PurchaseOrderStatus.PartiallyReceived,
        };

        var incomingRaw = await db.PurchaseOrderLines
            .Include(l => l.PurchaseOrder)
            .Where(l => partIds.Contains(l.PartId)
                && l.PurchaseOrder.DeletedAt == null
                && openStatuses.Contains(l.PurchaseOrder.Status))
            .Select(l => new
            {
                l.PartId,
                RemainingQty = (decimal)(l.OrderedQuantity - l.ReceivedQuantity),
                ExpectedDate = l.PurchaseOrder.ExpectedDeliveryDate,
            })
            .ToListAsync(cancellationToken);

        var incomingMap = incomingRaw
            .GroupBy(x => x.PartId)
            .ToDictionary(
                g => g.Key,
                g => new
                {
                    TotalQty = g.Sum(x => x.RemainingQty),
                    EarliestDate = g.Min(x => x.ExpectedDate),
                });

        var results = new List<BurnRateResponseModel>();

        foreach (var part in parts)
        {
            var stock = stockMap.TryGetValue(part.Id, out var s) ? s : null;
            var onHand = stock?.OnHand ?? 0m;
            var reserved = stock?.Reserved ?? 0m;
            var available = onHand - reserved;

            var incoming = incomingMap.TryGetValue(part.Id, out var inc) ? inc : null;
            var incomingQty = incoming?.TotalQty ?? 0m;
            var earliestArrival = incoming?.EarliestDate;

            var partMovements = rawMovements.Where(m => m.EntityId == part.Id).ToList();

            var burnRate30 = CalcBurnRate(partMovements, now, 30);
            var burnRate60 = CalcBurnRate(partMovements, now, 60);
            var burnRate90 = CalcBurnRate(partMovements, now, 90);

            // Use the widest available window for projections (most reliable)
            var bestBurnRate = burnRate90 ?? burnRate60 ?? burnRate30;

            decimal? daysRemaining = null;
            DateTimeOffset? projectedStockout = null;

            if (bestBurnRate.HasValue && bestBurnRate.Value > 0)
            {
                var effectiveStock = available + incomingQty;
                daysRemaining = effectiveStock / bestBurnRate.Value;
                projectedStockout = now.AddDays((double)daysRemaining.Value);
            }

            var effectiveLeadTime = sourcingByPart.TryGetValue(part.Id, out var sv)
                ? sv.LeadTimeDays
                : null;
            var needsReorder = DetermineNeedsReorder(part, effectiveLeadTime, available, incomingQty, bestBurnRate);

            if (request.NeedsReorderOnly && !needsReorder)
                continue;

            results.Add(new BurnRateResponseModel(
                part.Id,
                part.PartNumber,
                part.Description ?? part.Name,
                part.PreferredVendorId,
                part.PreferredVendor?.CompanyName,
                onHand,
                available,
                incomingQty,
                earliestArrival,
                burnRate30,
                burnRate60,
                burnRate90,
                daysRemaining,
                projectedStockout,
                part.MinStockThreshold,
                part.ReorderPoint,
                part.ReorderQuantity,
                effectiveLeadTime,
                part.SafetyStockDays,
                needsReorder));
        }

        return results;
    }

    private static decimal? CalcBurnRate(List<MovementRecord> movements, DateTimeOffset now, int windowDays)
    {
        var cutoff = now.AddDays(-windowDays);
        var total = movements
            .Where(m => m.MovedAt >= cutoff)
            .Sum(m => m.Quantity);

        return total > 0 ? total / windowDays : null;
    }

    private static bool DetermineNeedsReorder(
        Core.Entities.Part part, int? effectiveLeadTimeDays, decimal available, decimal incomingQty, decimal? burnRate)
    {
        // Explicit reorder point overrides everything
        if (part.ReorderPoint.HasValue)
            return (available + incomingQty) <= part.ReorderPoint.Value;

        // Burn-rate based: stock won't cover lead time + safety buffer
        if (burnRate.HasValue && burnRate.Value > 0)
        {
            var coverDays = (effectiveLeadTimeDays ?? 14) + (part.SafetyStockDays ?? 7);
            var neededStock = burnRate.Value * coverDays;
            return (available + incomingQty) < neededStock;
        }

        // Minimum threshold fallback
        if (part.MinStockThreshold.HasValue)
            return available <= part.MinStockThreshold.Value;

        return false;
    }
}
