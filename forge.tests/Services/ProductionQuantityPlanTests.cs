using FluentAssertions;

using Forge.Api.Features.Jobs;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Routing;
using Forge.Tests.Helpers;

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

    [Fact]
    public void A_linear_routing_plans_the_same_start_on_the_graph_as_in_step_order()
    {
        var steps = new List<RoutingStep> { new(1, 10), new(2, 20), new(3, 30) };
        var scrap = new Dictionary<int, decimal> { [1] = 0.02m, [2] = 0m, [3] = 0.05m };

        var planned = ProductionQuantityPlan.PlannedStepInputs(300m, steps, scrap, []);

        planned.Should().Equal(new Dictionary<int, decimal> { [1] = 322m, [2] = 315m, [3] = 315m });
        ProductionQuantityPlan.StartForRouting(300m, steps, scrap, [])
            .Should().Be(ProductionQuantityPlan.StartForRouting(300m, [0.02m, 0m, 0.05m]));
    }

    [Fact]
    public void Parallel_branches_compound_scrap_along_each_path_and_the_start_takes_the_larger_need()
    {
        var steps = new List<RoutingStep> { new(1, 10), new(2, 20), new(3, 30), new(4, 40) };
        var links = new List<RoutingLink>
        {
            new(2, 1, PredecessorGate.Quantity),
            new(3, 1, PredecessorGate.Quantity),
            new(4, 2, PredecessorGate.Quantity),
            new(4, 3, PredecessorGate.Quantity),
        };
        var scrap = new Dictionary<int, decimal> { [1] = 0m, [2] = 0.10m, [3] = 0.02m, [4] = 0m };

        var planned = ProductionQuantityPlan.PlannedStepInputs(100m, steps, scrap, links);

        planned[2].Should().Be(110m);
        planned[3].Should().Be(102m);
        planned[1].Should().Be(110m);
        ProductionQuantityPlan.StartForRouting(100m, steps, scrap, links).Should().Be(110m);
        ProductionQuantityPlan.StartForRouting(100m, [0m, 0.10m, 0.02m, 0m]).Should().Be(113m);
    }

    [Fact]
    public void Unjoined_parallel_ends_each_need_the_full_target()
    {
        var steps = new List<RoutingStep> { new(1, 10), new(2, 20), new(3, 30) };
        var links = new List<RoutingLink> { new(2, 1, PredecessorGate.Quantity), new(3, 1, PredecessorGate.Quantity) };
        var scrap = new Dictionary<int, decimal> { [1] = 0.05m, [2] = 0.10m, [3] = 0m };

        var planned = ProductionQuantityPlan.PlannedStepInputs(100m, steps, scrap, links);

        planned[2].Should().Be(110m);
        planned[3].Should().Be(100m);
        planned[1].Should().Be(116m);
    }

    [Theory]
    [InlineData(100, 14, 114)]
    [InlineData(100, 0, 100)]
    [InlineData(2.5, 3, 5.5)]
    public void Extra_pieces_start_the_target_plus_the_extras(decimal target, int extra, decimal expected) =>
        ProductionQuantityPlan.StartWithExtraPieces(target, extra).Should().Be(expected);

    [Fact]
    public void Only_jobs_made_for_demand_use_the_calculated_plan()
    {
        ProductionQuantityPlan.PlansForDemand(new Job()).Should().BeFalse();
        ProductionQuantityPlan.PlansForDemand(new Job { SalesOrderLineId = 1 }).Should().BeTrue();
        ProductionQuantityPlan.PlansForDemand(new Job { MrpPlannedOrderId = 1 }).Should().BeTrue();
        ProductionQuantityPlan.PlansForDemand(new Job { ParentJobId = 1 }).Should().BeTrue();
    }

    [Fact]
    public async Task The_start_plan_offers_the_calculated_extras_for_an_order_line_and_none_for_a_manual_job()
    {
        using var db = TestDbContextFactory.Create();
        db.Parts.Add(new Part { Id = 5, PartNumber = "P-5", ExpectedYieldPercent = 95m });
        await db.SaveChangesAsync();
        var handler = new GetJobStartPlanHandler(db);

        var forLine = await handler.Handle(new GetJobStartPlanQuery(5, 100m, SalesOrderLineId: 9), default);
        var manual = await handler.Handle(new GetJobStartPlanQuery(5, 100m), default);

        forLine.Should().Be(new Forge.Core.Models.JobStartPlanResponseModel(100m, 106m, 6));
        manual.Should().Be(new Forge.Core.Models.JobStartPlanResponseModel(100m, 100m, 0));
    }
}
