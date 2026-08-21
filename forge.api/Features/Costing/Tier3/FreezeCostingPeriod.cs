using System.Text.Json;

using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities.Costing;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models.Costing;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Costing.Tier3;

/// <summary>Freezes a costing period (spec §2.4): derive each pool's absorption rate from its budget,
/// then compose per-work-center rates from the pools that feed it. Reuses the work center's existing
/// flat labor/burden rate as the base labor/machine rate; the pools supply the overhead components.</summary>
public record FreezeCostingPeriodCommand(int PeriodId) : IRequest<FreezeCostingPeriodResultModel>;

public class FreezeCostingPeriodHandler(AppDbContext db, IClock clock)
    : IRequestHandler<FreezeCostingPeriodCommand, FreezeCostingPeriodResultModel>
{
    public async Task<FreezeCostingPeriodResultModel> Handle(FreezeCostingPeriodCommand request, CancellationToken ct)
    {
        var period = await db.CostingPeriods.FirstOrDefaultAsync(p => p.Id == request.PeriodId, ct)
            ?? throw new KeyNotFoundException($"Costing period {request.PeriodId} not found.");

        var now = clock.UtcNow.UtcDateTime;

        // 1. Derive each budgeted pool's absorption rate.
        var budgets = await db.OverheadPoolBudgets
            .Where(b => b.CostingPeriodId == period.Id)
            .ToListAsync(ct);
        foreach (var b in budgets)
            b.DerivedRate = b.BudgetDriverQty != 0 ? b.BudgetAmount / b.BudgetDriverQty : 0;
        var rateByPool = budgets.ToDictionary(b => b.OverheadCostPoolId, b => b.DerivedRate);

        // 2. Compose per-work-center rates from the pools that target each work center.
        var pools = await db.OverheadCostPools.AsNoTracking().ToListAsync(ct);
        var poolsByWorkCenter = pools.Where(p => p.WorkCenterId != null)
            .GroupBy(p => p.WorkCenterId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var workCenters = await db.WorkCenters.AsNoTracking().Where(w => w.IsActive).ToListAsync(ct);

        var existing = await db.WorkCenterCostRates.Where(r => r.CostingPeriodId == period.Id).ToListAsync(ct);
        db.WorkCenterCostRates.RemoveRange(existing);

        var frozenBy = db.CurrentUserId?.ToString();
        var rated = 0;
        foreach (var wc in workCenters)
        {
            decimal laborOh = 0, mchVar = 0, mchFixed = 0;
            var sourceIds = new List<int>();
            if (poolsByWorkCenter.TryGetValue(wc.Id, out var feeding))
            {
                foreach (var p in feeding)
                {
                    if (!rateByPool.TryGetValue(p.Id, out var rate)) continue;
                    sourceIds.Add(p.Id);
                    var fixedShare = p.Behavior switch
                    {
                        OverheadBehavior.Fixed => 1m,
                        OverheadBehavior.Semi => p.FixedPortion ?? 0m,
                        _ => 0m,
                    };
                    if (p.Driver == OverheadDriver.LaborHour)
                        laborOh += rate;
                    else
                    {
                        mchFixed += rate * fixedShare;
                        mchVar += rate * (1m - fixedShare);
                    }
                }
            }

            db.WorkCenterCostRates.Add(new WorkCenterCostRate
            {
                WorkCenterId = wc.Id,
                CostingPeriodId = period.Id,
                LaborRate = wc.LaborCostPerHour,
                LaborOhRate = laborOh,
                MachineRate = wc.BurdenRatePerHour,
                MachineOhVarRate = mchVar,
                MachineOhFixedRate = mchFixed,
                SourcePoolIds = sourceIds.Count > 0 ? JsonSerializer.Serialize(sourceIds) : null,
                FrozenAt = now,
                FrozenBy = frozenBy,
            });
            rated++;
        }

        period.Status = CostingPeriodStatus.Frozen;
        period.FrozenAt = now;

        db.LogActivityAt("frozen",
            $"Froze period: {budgets.Count} pool budget(s) rated, {rated} work-center rate(s) composed",
            ("CostingPeriod", period.Id));

        await db.SaveChangesAsync(ct);
        return new FreezeCostingPeriodResultModel(period.Id, budgets.Count, rated);
    }
}
