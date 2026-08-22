using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Costing.Tier3;
using Forge.Core.Entities;
using Forge.Core.Entities.Costing;
using Forge.Core.Enums;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Costing;

/// <summary>
/// Verifies the Tier-3 costing entities map to the forge-db schema on REAL Postgres (the EF
/// model↔schema check the InMemory provider can't give), and that <see cref="FreezeCostingPeriodHandler"/>
/// derives pool rates and composes per-work-center rates correctly against a live DB.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CostingTier3PostgresTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Freeze_derives_pool_rate_and_composes_work_center_rate()
    {
        int periodId, workCenterId;

        await using (var seed = fixture.CreateContext())
        {
            var wc = new WorkCenter
            {
                Name = "Freeze-PG 500-ton",
                Code = $"fz-{Guid.NewGuid():N}"[..12],
                LaborCostPerHour = 25m,
                BurdenRatePerHour = 40m,
                IsActive = true,
            };
            seed.WorkCenters.Add(wc);

            var cc = new CostingCostCenter { Code = $"CC{Guid.NewGuid():N}"[..8], Name = "Mold", Type = CostCenterType.Production, IsInventoriable = true };
            var period = new CostingPeriod { StartDate = DateTime.UtcNow.Date, EndDate = DateTime.UtcNow.Date.AddMonths(1), Status = CostingPeriodStatus.Open };
            seed.CostingCostCenters.Add(cc);
            seed.CostingPeriods.Add(period);
            await seed.SaveChangesAsync();

            var pool = new OverheadCostPool
            {
                CostingCostCenterId = cc.Id,
                WorkCenterId = wc.Id,
                Code = "MOLD-VAR",
                Name = "Mold variable",
                Behavior = OverheadBehavior.Variable,
                Driver = OverheadDriver.MachineHour,
            };
            seed.OverheadCostPools.Add(pool);
            await seed.SaveChangesAsync();

            // 8000 / 400 = 20.00 per machine hour.
            seed.OverheadPoolBudgets.Add(new OverheadPoolBudget
            {
                OverheadCostPoolId = pool.Id,
                CostingPeriodId = period.Id,
                BudgetAmount = 8000m,
                BudgetDriverQty = 400m,
            });
            await seed.SaveChangesAsync();

            periodId = period.Id;
            workCenterId = wc.Id;
        }

        await using (var act = fixture.CreateContext())
        {
            var result = await new FreezeCostingPeriodHandler(act, new SystemClock())
                .Handle(new FreezeCostingPeriodCommand(periodId), CancellationToken.None);

            result.BudgetsRated.Should().Be(1);
            result.WorkCentersRated.Should().BeGreaterThanOrEqualTo(1);
        }

        await using (var verify = fixture.CreateContext())
        {
            var period = await verify.CostingPeriods.SingleAsync(p => p.Id == periodId);
            period.Status.Should().Be(CostingPeriodStatus.Frozen);
            period.FrozenAt.Should().NotBeNull();

            var budget = await verify.OverheadPoolBudgets.SingleAsync(b => b.CostingPeriodId == periodId);
            budget.DerivedRate.Should().Be(20m);

            var rate = await verify.WorkCenterCostRates
                .SingleAsync(r => r.CostingPeriodId == periodId && r.WorkCenterId == workCenterId);
            rate.LaborRate.Should().Be(25m);        // from WorkCenter.LaborCostPerHour
            rate.MachineRate.Should().Be(40m);      // from WorkCenter.BurdenRatePerHour
            rate.MachineOhVarRate.Should().Be(20m); // variable pool on a machine-hour driver
            rate.MachineOhFixedRate.Should().Be(0m);
            rate.FrozenAt.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task Item_cost_burden_and_allocation_rule_round_trip()
    {
        int partId, periodId;

        await using (var seed = fixture.CreateContext())
        {
            var part = new Part { PartNumber = $"ISC-{Guid.NewGuid():N}"[..12], Description = "Std-cost part" };
            var period = new CostingPeriod { StartDate = DateTime.UtcNow.Date, EndDate = DateTime.UtcNow.Date.AddMonths(1), Status = CostingPeriodStatus.Open };
            seed.Parts.Add(part);
            seed.CostingPeriods.Add(period);
            await seed.SaveChangesAsync();
            partId = part.Id;
            periodId = period.Id;

            seed.ItemStandardCosts.Add(new ItemStandardCost
            {
                ItemId = partId,
                CostingPeriodId = periodId,
                ThisLevelJson = "{\"Mat\":6.30}",
                RolledUpJson = "{\"Mat\":6.30}",
                TotalStandard = 12.225m,
                StandardLotSize = 100m,
                RollVersion = 1,
            });
            seed.ItemBurdens.Add(new ItemBurden
            {
                ItemId = partId,
                CostingPeriodId = periodId,
                Element = CostElement.Moh,
                Basis = "pct_of_mat",
                Value = 0.10m,
            });
            seed.CostAllocationRules.Add(new CostAllocationRule
            {
                SourceAccountPattern = "Electricity%",
                Basis = AllocationBasis.Sqft,
            });
            await seed.SaveChangesAsync();
        }

        await using (var verify = fixture.CreateContext())
        {
            (await verify.ItemStandardCosts.SingleAsync(x => x.ItemId == partId)).TotalStandard.Should().Be(12.225m);
            (await verify.ItemBurdens.SingleAsync(x => x.ItemId == partId)).Element.Should().Be(CostElement.Moh);
            (await verify.CostAllocationRules.CountAsync(x => x.SourceAccountPattern == "Electricity%")).Should().BeGreaterThanOrEqualTo(1);
        }
    }
}
