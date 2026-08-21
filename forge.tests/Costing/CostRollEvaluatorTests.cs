using Forge.Core.Costing;
using Forge.Core.Enums;

namespace Forge.Tests.Costing;

/// <summary>Verifies the pure standard-cost roll (spec §3.1) against a worked fixture: setup
/// amortization, purchased material with scrap, an expensed component, per-element labor/machine
/// overhead, and phantom absorption at the parent level.</summary>
public class CostRollEvaluatorTests
{
    private static readonly CostRollWorkCenterRate Wc500 =
        new(LaborRate: 25m, LaborOhRate: 10m, MachineRate: 40m, MachineOhVarRate: 8m, MachineOhFixedRate: 12m);

    private static CostElementAmounts NeverRolledUp(int id) =>
        throw new InvalidOperationException($"rolledUpOf unexpectedly called for {id}");

    [Fact]
    public void Roll_DecomposesElements_WithSetupAmortizationAndScrap()
    {
        var item = new CostRollItem(
            ItemId: 1,
            StandardLotSize: 100m,
            Bom:
            [
                new CostRollBomLine(10, QtyPer: 2m, ScrapPct: 0.05m, BomComponentType.Stocked, ComponentIsManufactured: false),
                new CostRollBomLine(20, QtyPer: 1m, ScrapPct: 0m, BomComponentType.Expensed, ComponentIsManufactured: false),
            ],
            Routing:
            [
                new CostRollRoutingOp(WorkCenterId: 1, SetupHours: 2m, RunHoursPerUnit: 0.05m, LaborCrew: 0.5m, SubcontractStdCost: 0m),
            ],
            Burdens: []);

        decimal PurchasedStd(int id) => id switch { 10 => 3.00m, 20 => 0.50m, _ => 0m };

        var result = CostRollEvaluator.Roll(item, _ => null, PurchasedStd, NeverRolledUp, _ => Wc500);

        // setupPerUnit = 2/100 = 0.02; mchHrs = 0.07; labHrs = 0.035
        Assert.Equal(6.30m, result.RolledUp.Mat, 4);   // 3.00 * 2 * 1.05
        Assert.Equal(0.50m, result.RolledUp.Moh, 4);   // expensed
        Assert.Equal(0.875m, result.RolledUp.Lab, 4);  // 0.035 * 25
        Assert.Equal(0.35m, result.RolledUp.Loh, 4);   // 0.035 * 10
        Assert.Equal(2.80m, result.RolledUp.Mch, 4);   // 0.07 * 40
        Assert.Equal(0.56m, result.RolledUp.Mohv, 4);  // 0.07 * 8
        Assert.Equal(0.84m, result.RolledUp.Mohf, 4);  // 0.07 * 12
        Assert.Equal(0m, result.RolledUp.Sub, 4);
        Assert.Equal(12.225m, result.TotalStandard, 4);
    }

    [Fact]
    public void Roll_AbsorbsPhantomCost_AtParentLevel()
    {
        var phantom = new CostRollItem(
            ItemId: 2,
            StandardLotSize: 1m,
            Bom: [new CostRollBomLine(30, QtyPer: 3m, ScrapPct: 0m, BomComponentType.Stocked, ComponentIsManufactured: false)],
            Routing: [],
            Burdens: []);

        var parent = new CostRollItem(
            ItemId: 1,
            StandardLotSize: 1m,
            Bom: [new CostRollBomLine(2, QtyPer: 2m, ScrapPct: 0m, BomComponentType.Phantom, ComponentIsManufactured: true)],
            Routing: [],
            Burdens: []);

        decimal PurchasedStd(int id) => id == 30 ? 1.00m : 0m;

        var result = CostRollEvaluator.Roll(
            parent, itemOf: id => id == 2 ? phantom : null, PurchasedStd, NeverRolledUp, _ => Wc500);

        // phantom rolls up to Mat 3.00 (1.00 * 3); parent absorbs × 2 at THIS level → 6.00
        Assert.Equal(6.00m, result.ThisLevel.Mat, 4);
        Assert.Equal(6.00m, result.TotalStandard, 4);
    }

    [Fact]
    public void Roll_AppliesItemBurden_PctOfMaterialAndPerUnit()
    {
        var item = new CostRollItem(
            ItemId: 1,
            StandardLotSize: 1m,
            Bom: [new CostRollBomLine(10, QtyPer: 1m, ScrapPct: 0m, BomComponentType.Stocked, ComponentIsManufactured: false)],
            Routing: [],
            Burdens:
            [
                new CostRollBurden(CostElement.Moh, "pct_of_mat", 0.10m),  // 10% of material
                new CostRollBurden(CostElement.Mohf, "per_unit", 0.25m),
            ]);

        var result = CostRollEvaluator.Roll(item, _ => null, _ => 10.00m, NeverRolledUp, _ => Wc500);

        Assert.Equal(10.00m, result.RolledUp.Mat, 4);
        Assert.Equal(1.00m, result.RolledUp.Moh, 4);   // 10% of 10.00
        Assert.Equal(0.25m, result.RolledUp.Mohf, 4);
        Assert.Equal(11.25m, result.TotalStandard, 4);
    }
}
