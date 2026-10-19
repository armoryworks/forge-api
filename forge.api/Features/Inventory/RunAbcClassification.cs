using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Inventory;

public record RunAbcClassificationCommand(AbcClassificationParametersModel Parameters) : IRequest<AbcClassificationRunResponseModel>;

public class RunAbcClassificationHandler(AppDbContext db, IClock clock) : IRequestHandler<RunAbcClassificationCommand, AbcClassificationRunResponseModel>
{
    private const int MaxBomDepth = 20;

    public async Task<AbcClassificationRunResponseModel> Handle(RunAbcClassificationCommand command, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var lookbackStart = now.AddMonths(-command.Parameters.LookbackMonths);

        var partIds = await db.Parts
            .AsNoTracking()
            .Where(p => p.Status == PartStatus.Active)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        var consumption = await ConsumptionQuantitiesAsync(lookbackStart, cancellationToken);
        var unitCosts = await UnitCostsAsync(cancellationToken);

        var classifications = partIds
            .Select(partId =>
            {
                var quantity = consumption.GetValueOrDefault(partId);
                var unitCost = unitCosts.GetValueOrDefault(partId);
                return new
                {
                    PartId = partId,
                    AnnualUsageValue = quantity * unitCost,
                    AnnualDemandQuantity = quantity,
                    UnitCost = unitCost,
                };
            })
            .OrderByDescending(x => x.AnnualUsageValue)
            .ToList();

        var totalUsageValue = classifications.Sum(c => c.AnnualUsageValue);

        var run = new AbcClassificationRun
        {
            RunDate = now,
            TotalParts = classifications.Count,
            ClassAThresholdPercent = command.Parameters.ClassAThresholdPercent,
            ClassBThresholdPercent = command.Parameters.ClassBThresholdPercent,
            TotalAnnualUsageValue = totalUsageValue,
            LookbackMonths = command.Parameters.LookbackMonths,
        };

        db.AbcClassificationRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);

        var cumulativeValue = 0m;
        var rank = 0;
        var classACount = 0;
        var classBCount = 0;
        var classCCount = 0;

        foreach (var item in classifications)
        {
            rank++;
            var percentBefore = totalUsageValue > 0 ? (cumulativeValue / totalUsageValue) * 100 : 0;
            cumulativeValue += item.AnnualUsageValue;
            var cumulativePercent = totalUsageValue > 0 ? (cumulativeValue / totalUsageValue) * 100 : 0;

            var abcClass = item.AnnualUsageValue <= 0 ? AbcClass.C
                : percentBefore < command.Parameters.ClassAThresholdPercent ? AbcClass.A
                : percentBefore < command.Parameters.ClassBThresholdPercent ? AbcClass.B
                : AbcClass.C;

            switch (abcClass)
            {
                case AbcClass.A: classACount++; break;
                case AbcClass.B: classBCount++; break;
                case AbcClass.C: classCCount++; break;
            }

            db.AbcClassifications.Add(new AbcClassification
            {
                PartId = item.PartId,
                Classification = abcClass,
                AnnualUsageValue = item.AnnualUsageValue,
                AnnualDemandQuantity = item.AnnualDemandQuantity,
                UnitCost = item.UnitCost,
                CumulativePercent = cumulativePercent,
                Rank = rank,
                CalculatedAt = now,
                RunId = run.Id,
            });
        }

        run.ClassACount = classACount;
        run.ClassBCount = classBCount;
        run.ClassCCount = classCCount;

        await db.SaveChangesAsync(cancellationToken);

        return new AbcClassificationRunResponseModel
        {
            Id = run.Id,
            RunDate = run.RunDate,
            TotalParts = run.TotalParts,
            ClassACount = run.ClassACount,
            ClassBCount = run.ClassBCount,
            ClassCCount = run.ClassCCount,
            ClassAThresholdPercent = run.ClassAThresholdPercent,
            ClassBThresholdPercent = run.ClassBThresholdPercent,
            TotalAnnualUsageValue = run.TotalAnnualUsageValue,
            LookbackMonths = run.LookbackMonths,
        };
    }

    private async Task<Dictionary<int, decimal>> ConsumptionQuantitiesAsync(DateTimeOffset lookbackStart, CancellationToken cancellationToken)
    {
        var shipped = await db.ShipmentLines
            .AsNoTracking()
            .Where(sl => sl.Shipment.ShippedDate != null && sl.Shipment.ShippedDate >= lookbackStart
                && (sl.PartId != null || (sl.SalesOrderLine != null && sl.SalesOrderLine.PartId != null)))
            .Select(sl => new { PartId = sl.PartId ?? sl.SalesOrderLine!.PartId!.Value, sl.Quantity })
            .ToListAsync(cancellationToken);

        var issued = await db.MaterialIssues
            .AsNoTracking()
            .Where(mi => mi.IssuedAt >= lookbackStart)
            .Select(mi => new { mi.PartId, Quantity = mi.IssueType == MaterialIssueType.Return ? -mi.Quantity : mi.Quantity })
            .ToListAsync(cancellationToken);

        var history = shipped.Select(x => (x.PartId, x.Quantity))
            .Concat(issued.Select(x => (x.PartId, x.Quantity)))
            .GroupBy(x => x.PartId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));

        var openDemand = await ExplodedOpenDemandAsync(cancellationToken);

        var result = new Dictionary<int, decimal>();
        foreach (var partId in history.Keys.Concat(openDemand.Keys).Distinct())
        {
            var historical = history.GetValueOrDefault(partId);
            var quantity = historical > 0 ? historical : openDemand.GetValueOrDefault(partId);
            if (quantity > 0)
                result[partId] = quantity;
        }

        return result;
    }

    private async Task<Dictionary<int, decimal>> ExplodedOpenDemandAsync(CancellationToken cancellationToken)
    {
        var openLines = await db.SalesOrderLines
            .AsNoTracking()
            .Where(l => l.PartId != null
                && l.SalesOrder!.Status != SalesOrderStatus.Cancelled
                && l.SalesOrder!.Status != SalesOrderStatus.Completed
                && l.Quantity - l.ShippedQuantity > 0)
            .Select(l => new { PartId = l.PartId!.Value, Quantity = l.Quantity - l.ShippedQuantity })
            .ToListAsync(cancellationToken);

        var bomByParent = (await db.BOMLines
            .AsNoTracking()
            .Select(b => new { b.ParentPartId, b.ChildPartId, b.Quantity })
            .ToListAsync(cancellationToken))
            .GroupBy(b => b.ParentPartId)
            .ToDictionary(g => g.Key, g => g.Select(b => (b.ChildPartId, b.Quantity)).ToList());

        var demand = new Dictionary<int, decimal>();
        var pending = new Stack<(int PartId, decimal Quantity, int Depth)>(
            openLines.Select(l => (l.PartId, (decimal)l.Quantity, 0)));

        while (pending.Count > 0)
        {
            var (partId, quantity, depth) = pending.Pop();
            demand[partId] = demand.GetValueOrDefault(partId) + quantity;

            if (depth >= MaxBomDepth || !bomByParent.TryGetValue(partId, out var children))
                continue;

            foreach (var (childPartId, perParent) in children)
                pending.Push((childPartId, quantity * perParent, depth + 1));
        }

        return demand;
    }

    private async Task<Dictionary<int, decimal>> UnitCostsAsync(CancellationToken cancellationToken)
    {
        var standardCosts = await db.Parts
            .AsNoTracking()
            .Select(p => new
            {
                p.Id,
                Standard = p.ManualCostOverride ?? (p.CurrentCostCalculation != null ? p.CurrentCostCalculation.ResultAmount : (decimal?)null),
            })
            .Where(p => p.Standard != null && p.Standard > 0)
            .ToDictionaryAsync(p => p.Id, p => p.Standard!.Value, cancellationToken);

        var latestPoCosts = await db.PurchaseOrderLines
            .AsNoTracking()
            .Where(pol => pol.PartId != null && pol.UnitPrice > 0)
            .GroupBy(pol => pol.PartId!.Value)
            .Select(g => new { PartId = g.Key, UnitCost = g.OrderByDescending(pol => pol.Id).First().UnitPrice })
            .ToDictionaryAsync(x => x.PartId, x => x.UnitCost, cancellationToken);

        var latestSalePrices = await db.SalesOrderLines
            .AsNoTracking()
            .Where(sol => sol.PartId != null && sol.UnitPrice > 0)
            .GroupBy(sol => sol.PartId!.Value)
            .Select(g => new { PartId = g.Key, UnitPrice = g.OrderByDescending(sol => sol.Id).First().UnitPrice })
            .ToDictionaryAsync(x => x.PartId, x => x.UnitPrice, cancellationToken);

        var costs = new Dictionary<int, decimal>(latestSalePrices);
        foreach (var (partId, cost) in latestPoCosts)
            costs[partId] = cost;
        foreach (var (partId, cost) in standardCosts)
            costs[partId] = cost;
        return costs;
    }
}
