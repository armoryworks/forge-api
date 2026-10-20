using FluentAssertions;

using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;

namespace Forge.Tests.Services;

public class ProductionQuantityPlanTests
{
    [Theory]
    [InlineData(300, 95, 316)]
    [InlineData(100, 90, 112)]
    [InlineData(100, 100, 100)]
    [InlineData(1, 99.5, 2)]
    public void Yield_divides_the_target_and_rounds_up_to_whole_units(decimal target, decimal yield, decimal expected) =>
        ProductionQuantityPlan.StartForYield(target, yield).Should().Be(expected);

    [Fact]
    public void No_yield_on_the_part_means_no_overage_even_for_fractional_quantities() =>
        ProductionQuantityPlan.StartForYield(2.5m, null).Should().Be(2.5m);

    [Fact]
    public void Routing_scrap_plans_each_step_backwards_from_the_target()
    {
        var planned = ProductionQuantityPlan.PlannedStepInputs(300m, [0.02m, 0m, 0.05m]);

        planned.Should().Equal(322m, 315m, 315m);
        ProductionQuantityPlan.StartForRouting(300m, [0.02m, 0m, 0.05m]).Should().Be(322m);
    }

    [Fact]
    public void A_routing_without_scrap_plans_the_target_at_every_step() =>
        ProductionQuantityPlan.PlannedStepInputs(300m, [0m, 0m]).Should().Equal(300m, 300m);

    [Fact]
    public void When_yield_and_routing_scrap_both_exist_the_larger_plan_wins_not_the_product()
    {
        ProductionQuantityPlan.Start(300m, 95m, [0.02m, 0.05m]).Should().Be(322m);
        ProductionQuantityPlan.Start(300m, 90m, [0.02m, 0.05m]).Should().Be(334m);
    }

    [Fact]
    public void An_mrp_planned_part_applies_its_order_modifiers_after_the_overage()
    {
        var part = new Part { IsMrpPlanned = true, LotSizingRule = LotSizingRule.MultiplesOf, OrderMultiple = 50m };
        ProductionQuantityPlan.Start(300m, 95m, [], part).Should().Be(350m);

        var minimum = new Part { IsMrpPlanned = true, MinimumOrderQuantity = 500m };
        ProductionQuantityPlan.Start(300m, 95m, [], minimum).Should().Be(500m);
    }

    [Fact]
    public void Order_modifiers_are_ignored_on_parts_mrp_does_not_plan()
    {
        var part = new Part { IsMrpPlanned = false, MinimumOrderQuantity = 500m };
        ProductionQuantityPlan.Start(300m, null, [], part).Should().Be(300m);
    }
}
