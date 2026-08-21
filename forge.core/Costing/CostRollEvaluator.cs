using Forge.Core.Enums;

namespace Forge.Core.Costing;

/// <summary>
/// Pure standard-cost roll (spec §3.1). Given one item plus resolvers for its components, purchased
/// standards, sub-assembly rolled-up costs, and work-center rates, produces the eight-element this-level
/// and rolled-up decomposition. No I/O, no storage, no clock — fully unit-testable and idempotent.
/// </summary>
public static class CostRollEvaluator
{
    /// <summary>Roll one item's standard cost.</summary>
    /// <param name="item">The item being rolled.</param>
    /// <param name="itemOf">Resolves a phantom component id to its own <see cref="CostRollItem"/> (for recursion). May return null.</param>
    /// <param name="purchasedStdOf">Resolves a purchased/expensed component id to its unit standard cost.</param>
    /// <param name="rolledUpOf">Resolves a manufactured sub-assembly id to its already-rolled-up elements.</param>
    /// <param name="rateOf">Resolves a work-center id to its frozen rates.</param>
    public static CostRollResult Roll(
        CostRollItem item,
        Func<int, CostRollItem?> itemOf,
        Func<int, decimal> purchasedStdOf,
        Func<int, CostElementAmounts> rolledUpOf,
        Func<int, CostRollWorkCenterRate> rateOf)
    {
        var thisLevel = CostElementAmounts.Zero;
        var lower = CostElementAmounts.Zero;

        foreach (var line in item.Bom)
        {
            if (line.ComponentType == BomComponentType.Phantom) continue;

            var qty = line.QtyPer * (1 + line.ScrapPct);
            if (line.ComponentType == BomComponentType.Expensed)
            {
                // Expensed components roll into material overhead — no inventory movement.
                thisLevel = thisLevel with { Moh = thisLevel.Moh + purchasedStdOf(line.ComponentItemId) * qty };
            }
            else if (!line.ComponentIsManufactured)
            {
                // Purchased stocked component → direct material.
                thisLevel = thisLevel with { Mat = thisLevel.Mat + purchasedStdOf(line.ComponentItemId) * qty };
            }
            else
            {
                // Manufactured sub-assembly → its rolled-up cost lands at the lower level, scaled by qty.
                lower += rolledUpOf(line.ComponentItemId) * qty;
            }
        }

        // Phantoms have no inventory identity: their cost is absorbed at THIS level.
        foreach (var line in item.Bom)
        {
            if (line.ComponentType != BomComponentType.Phantom) continue;
            var phantom = itemOf(line.ComponentItemId);
            if (phantom is null) continue;
            var qty = line.QtyPer * (1 + line.ScrapPct);
            var rolled = Roll(phantom, itemOf, purchasedStdOf, rolledUpOf, rateOf);
            thisLevel += rolled.RolledUp * qty;
        }

        foreach (var op in item.Routing)
        {
            var r = rateOf(op.WorkCenterId);
            var setupPerUnit = item.StandardLotSize > 0 ? op.SetupHours / item.StandardLotSize : 0;
            var mchHrs = op.RunHoursPerUnit + setupPerUnit;
            var labHrs = mchHrs * op.LaborCrew;
            thisLevel = thisLevel with
            {
                Lab = thisLevel.Lab + labHrs * r.LaborRate,
                Loh = thisLevel.Loh + labHrs * r.LaborOhRate,
                Mch = thisLevel.Mch + mchHrs * r.MachineRate,
                Mohv = thisLevel.Mohv + mchHrs * r.MachineOhVarRate,
                Mohf = thisLevel.Mohf + mchHrs * r.MachineOhFixedRate,
                Sub = thisLevel.Sub + op.SubcontractStdCost,
            };
        }

        foreach (var b in item.Burdens)
        {
            var amount = string.Equals(b.Basis, "pct_of_mat", StringComparison.OrdinalIgnoreCase)
                ? thisLevel.Mat * b.Value
                : b.Value;
            thisLevel = thisLevel.Add(b.Element, amount);
        }

        var rolledUp = thisLevel + lower;
        return new CostRollResult(thisLevel, rolledUp, rolledUp.Total);
    }
}
