using Forge.Core.Enums;

namespace Forge.Api.Services;

public static class LotSizer
{
    public static decimal Apply(
        LotSizingRule rule,
        decimal netRequirement,
        decimal? fixedOrderQuantity,
        decimal? minimumOrderQuantity,
        decimal? orderMultiple)
    {
        if (netRequirement <= 0) return 0;

        var qty = rule switch
        {
            LotSizingRule.LotForLot => netRequirement,
            LotSizingRule.FixedQuantity => ApplyFixedQuantity(netRequirement, fixedOrderQuantity ?? netRequirement),
            LotSizingRule.MinMax => ApplyMinMax(netRequirement, minimumOrderQuantity ?? netRequirement),
            LotSizingRule.EconomicOrderQuantity => ApplyFixedQuantity(netRequirement, fixedOrderQuantity ?? netRequirement),
            LotSizingRule.MultiplesOf => ApplyMultiplesOf(netRequirement, orderMultiple ?? 1),
            _ => netRequirement,
        };

        if (minimumOrderQuantity.HasValue && qty < minimumOrderQuantity.Value)
            qty = minimumOrderQuantity.Value;

        return qty;
    }

    private static decimal ApplyFixedQuantity(decimal netRequirement, decimal fixedQty)
    {
        if (fixedQty <= 0) return netRequirement;
        var lots = Math.Ceiling(netRequirement / fixedQty);
        return lots * fixedQty;
    }

    private static decimal ApplyMinMax(decimal netRequirement, decimal minimumQty)
    {
        return netRequirement < minimumQty ? minimumQty : netRequirement;
    }

    private static decimal ApplyMultiplesOf(decimal netRequirement, decimal multiple)
    {
        if (multiple <= 0) return netRequirement;
        var lots = Math.Ceiling(netRequirement / multiple);
        return lots * multiple;
    }
}
