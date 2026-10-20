using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Jobs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Services;

public static class ProductionQuantityPlan
{
    public static decimal StartForYield(decimal target, decimal? expectedYieldPercent) =>
        expectedYieldPercent is decimal yield && yield > 0m && yield < 100m
            ? Math.Ceiling(target * 100m / yield)
            : target;

    public static IReadOnlyList<decimal> PlannedStepInputs(decimal target, IReadOnlyList<decimal> scrapFactorsInStepOrder)
    {
        var planned = new decimal[scrapFactorsInStepOrder.Count];
        var output = target;
        for (var i = scrapFactorsInStepOrder.Count - 1; i >= 0; i--)
        {
            var scrap = scrapFactorsInStepOrder[i];
            output = scrap > 0m ? Math.Ceiling(output * (1m + scrap)) : output;
            planned[i] = output;
        }
        return planned;
    }

    public static decimal StartForRouting(decimal target, IReadOnlyList<decimal> scrapFactorsInStepOrder)
    {
        var planned = PlannedStepInputs(target, scrapFactorsInStepOrder);
        return planned.Count > 0 ? planned[0] : target;
    }

    public static decimal Start(
        decimal target,
        decimal? expectedYieldPercent,
        IReadOnlyList<decimal> scrapFactorsInStepOrder,
        Part? lotSizing = null)
    {
        var start = Math.Max(
            StartForYield(target, expectedYieldPercent),
            StartForRouting(target, scrapFactorsInStepOrder));

        if (lotSizing is { IsMrpPlanned: true })
            start = LotSizer.Apply(
                lotSizing.LotSizingRule ?? LotSizingRule.LotForLot,
                start,
                lotSizing.FixedOrderQuantity,
                lotSizing.MinimumOrderQuantity,
                lotSizing.OrderMultiple);

        return JobQuantityRules.Fit(Math.Max(start, target));
    }

    public static async Task<decimal> StartForPartAsync(
        AppDbContext db, int partId, decimal target, CancellationToken ct)
    {
        var part = await db.Parts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == partId, ct);
        if (part is null)
            return target;

        var scrapFactors = await db.Operations
            .Where(o => o.PartId == partId)
            .OrderBy(o => o.StepNumber).ThenBy(o => o.Id)
            .Select(o => o.ScrapFactor)
            .ToListAsync(ct);
        return Start(target, part.ExpectedYieldPercent, scrapFactors, part);
    }
}
