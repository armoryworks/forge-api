using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Inventory;

public record RunAbcClassificationCommand(AbcClassificationParametersModel Parameters) : IRequest<AbcClassificationRunResponseModel>;

public class RunAbcClassificationHandler(AppDbContext db, IClock clock) : IRequestHandler<RunAbcClassificationCommand, AbcClassificationRunResponseModel>
{
    public async Task<AbcClassificationRunResponseModel> Handle(RunAbcClassificationCommand command, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var lookbackStart = now.AddMonths(-command.Parameters.LookbackMonths);

        // Get all active parts with their usage data from sales order lines
        var partUsage = await db.SalesOrderLines
            .AsNoTracking()
            .Include(sol => sol.SalesOrder)
            .Where(sol => sol.SalesOrder!.CreatedAt >= lookbackStart)
            .GroupBy(sol => sol.PartId)
            .Select(g => new
            {
                PartId = g.Key,
                TotalQuantity = g.Sum(sol => sol.Quantity),
                TotalValue = g.Sum(sol => sol.Quantity * sol.UnitPrice),
            })
            .ToListAsync(cancellationToken);

        var partIds = await db.Parts
            .AsNoTracking()
            .Where(p => p.Status == PartStatus.Active)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        // Get latest unit price per part from purchase order lines as cost proxy
        var latestCosts = await db.PurchaseOrderLines
            .AsNoTracking()
            .GroupBy(pol => pol.PartId)
            .Select(g => new { PartId = g.Key, UnitCost = g.OrderByDescending(pol => pol.Id).First().UnitPrice })
            .ToDictionaryAsync(x => x.PartId, x => x.UnitCost, cancellationToken);

        var usageLookup = partUsage.ToDictionary(u => u.PartId ?? 0);
        var totalUsageValue = partUsage.Sum(u => u.TotalValue);

        var classifications = partIds
            .Select(partId =>
            {
                usageLookup.TryGetValue(partId, out var usage);
                latestCosts.TryGetValue(partId, out var unitCost);
                return new
                {
                    PartId = partId,
                    AnnualUsageValue = usage?.TotalValue ?? 0,
                    AnnualDemandQuantity = usage?.TotalQuantity ?? 0m,
                    UnitCost = unitCost,
                };
            })
            .OrderByDescending(x => x.AnnualUsageValue)
            .ToList();

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
            cumulativeValue += item.AnnualUsageValue;
            var cumulativePercent = totalUsageValue > 0 ? (cumulativeValue / totalUsageValue) * 100 : 0;

            var abcClass = cumulativePercent <= command.Parameters.ClassAThresholdPercent ? AbcClass.A
                : cumulativePercent <= command.Parameters.ClassBThresholdPercent ? AbcClass.B
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
}
