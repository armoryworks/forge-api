using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Jobs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Routing;
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

    public static IReadOnlyDictionary<int, decimal> PlannedStepInputs(
        decimal target,
        IReadOnlyList<RoutingStep> steps,
        IReadOnlyDictionary<int, decimal> scrapFactors,
        IReadOnlyList<RoutingLink> links)
    {
        var predecessors = RoutingPredecessors.Effective(steps, links);
        var successors = predecessors
            .SelectMany(p => p.Value.Select(l => (Successor: p.Key, l.PredecessorOperationId)))
            .ToLookup(l => l.PredecessorOperationId, l => l.Successor);

        var planned = new Dictionary<int, decimal>();
        foreach (var step in steps.OrderByDescending(s => s.StepNumber).ThenByDescending(s => s.OperationId))
        {
            if (planned.ContainsKey(step.OperationId))
                continue;
            var next = successors[step.OperationId].Where(planned.ContainsKey).ToList();
            var output = next.Count == 0 ? target : next.Max(id => planned[id]);
            var scrap = scrapFactors.GetValueOrDefault(step.OperationId);
            planned[step.OperationId] = scrap > 0m ? Math.Ceiling(output * (1m + scrap)) : output;
        }
        return planned;
    }

    public static decimal StartForRouting(
        decimal target,
        IReadOnlyList<RoutingStep> steps,
        IReadOnlyDictionary<int, decimal> scrapFactors,
        IReadOnlyList<RoutingLink> links)
    {
        var planned = PlannedStepInputs(target, steps, scrapFactors, links);
        return planned.Count > 0 ? Math.Max(target, planned.Values.Max()) : target;
    }

    public static decimal Start(
        decimal target,
        decimal? expectedYieldPercent,
        IReadOnlyList<decimal> scrapFactorsInStepOrder,
        Part? lotSizing = null)
        => Start(target, expectedYieldPercent, StartForRouting(target, scrapFactorsInStepOrder), lotSizing);

    private static decimal Start(decimal target, decimal? expectedYieldPercent, decimal routingStart, Part? lotSizing)
    {
        var start = Math.Max(StartForYield(target, expectedYieldPercent), routingStart);

        if (lotSizing is { IsMrpPlanned: true })
            start = LotSizer.Apply(
                lotSizing.LotSizingRule ?? LotSizingRule.LotForLot,
                start,
                lotSizing.FixedOrderQuantity,
                lotSizing.MinimumOrderQuantity,
                lotSizing.OrderMultiple);

        return JobQuantityRules.Fit(Math.Max(start, target));
    }

    public static decimal StartWithExtraPieces(decimal target, int extraPieces) =>
        JobQuantityRules.Fit(target + Math.Max(0, extraPieces));

    public static bool PlansForDemand(Job job) =>
        job.SalesOrderLineId is not null || job.MrpPlannedOrderId is not null || job.ParentJobId is not null;

    public static async Task<decimal> CalculatedStartAsync(
        AppDbContext db, Job job, int partId, decimal target, CancellationToken ct) =>
        PlansForDemand(job)
            ? await StartForPartAsync(db, partId, target, ct)
            : JobQuantityRules.Fit(target);

    public static async Task<decimal> StartForPartAsync(
        AppDbContext db, int partId, decimal target, CancellationToken ct)
    {
        var part = await db.Parts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == partId, ct);
        if (part is null)
            return target;

        var routing = await db.Operations
            .Where(o => o.PartId == partId)
            .Select(o => new { o.Id, o.StepNumber, o.ScrapFactor })
            .ToListAsync(ct);
        var steps = routing.Select(o => new RoutingStep(o.Id, o.StepNumber)).ToList();
        var links = await StepGateService.LoadLinksAsync(db, steps.Select(s => s.OperationId), ct);
        var routingStart = StartForRouting(target, steps, routing.ToDictionary(o => o.Id, o => o.ScrapFactor), links);
        return Start(target, part.ExpectedYieldPercent, routingStart, part);
    }
}
