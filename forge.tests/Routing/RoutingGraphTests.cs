using FluentAssertions;

using Forge.Core.Enums;
using Forge.Core.Routing;

namespace Forge.Tests.Routing;

public class RoutingGraphTests
{
    private static readonly List<int> Routing = [1, 2, 3, 4];

    private static RoutingGraphStep Step(int op, int stepNumber, params int[] follows)
        => new(op, stepNumber, follows.Select(p => new RoutingLink(op, p, PredecessorGate.Quantity)).ToList());

    [Fact]
    public void A_diamond_with_lower_numbered_predecessors_is_valid()
    {
        var issues = RoutingGraph.Validate(Routing, [Step(1, 10), Step(2, 20, 1), Step(3, 30, 1), Step(4, 40, 2, 3)], 50);

        issues.Should().BeEmpty();
    }

    [Fact]
    public void Steps_may_share_a_step_number_and_a_step_with_no_links_is_valid()
    {
        var issues = RoutingGraph.Validate(Routing, [Step(1, 10), Step(2, 20), Step(3, 20, 1), Step(4, 30)], 50);

        issues.Should().BeEmpty();
    }

    [Fact]
    public void A_cycle_is_refused_because_one_of_its_links_points_up()
    {
        var issues = RoutingGraph.Validate(Routing, [Step(1, 10), Step(2, 20, 3), Step(3, 30, 2), Step(4, 40)], 50);

        issues.Should().ContainSingle().Which.Should().Be(
            new RoutingGraphIssue(RoutingGraphIssueKind.PredecessorNotEarlier, 2, 20, 3, 30));
    }

    [Fact]
    public void A_predecessor_with_the_same_step_number_is_refused()
    {
        var issues = RoutingGraph.Validate(Routing, [Step(1, 10), Step(2, 20), Step(3, 20, 2), Step(4, 40)], 50);

        issues.Should().ContainSingle().Which.Kind.Should().Be(RoutingGraphIssueKind.PredecessorNotEarlier);
    }

    [Fact]
    public void The_check_uses_the_new_step_numbers_so_a_reorder_and_its_links_save_together()
    {
        var issues = RoutingGraph.Validate(Routing, [Step(1, 10), Step(2, 30, 3), Step(3, 20, 1), Step(4, 40, 2)], 50);

        issues.Should().BeEmpty();
    }

    [Fact]
    public void Self_links_unknown_predecessors_and_bad_step_numbers_are_each_reported()
    {
        var issues = RoutingGraph.Validate(Routing, [Step(1, 0), Step(2, 20, 2), Step(3, 30, 99), Step(4, 40)], 50);

        issues.Select(i => i.Kind).Should().BeEquivalentTo([
            RoutingGraphIssueKind.InvalidStepNumber,
            RoutingGraphIssueKind.SelfLink,
            RoutingGraphIssueKind.UnknownPredecessor,
        ]);
    }

    [Fact]
    public void Every_live_step_must_be_sent_exactly_once_and_no_other()
    {
        var issues = RoutingGraph.Validate(Routing, [Step(1, 10), Step(2, 20), Step(2, 25), Step(3, 30), Step(7, 70)], 50);

        issues.Select(i => (i.Kind, i.OperationId)).Should().BeEquivalentTo([
            (RoutingGraphIssueKind.DuplicateStep, 2),
            (RoutingGraphIssueKind.UnknownStep, 7),
            (RoutingGraphIssueKind.MissingStep, 4),
        ]);
    }

    [Fact]
    public void A_link_to_a_step_of_another_routing_is_unknown()
    {
        var issues = RoutingGraph.Validate(Routing, [Step(1, 10), Step(2, 20, 1), Step(3, 30, 8), Step(4, 40)], 50);

        issues.Should().ContainSingle().Which.Should().Be(
            new RoutingGraphIssue(RoutingGraphIssueKind.UnknownPredecessor, 3, 30, 8));
    }

    [Fact]
    public void Too_many_predecessors_are_refused_and_repeated_links_count_once()
    {
        var many = RoutingGraph.Validate(Routing, [Step(1, 10), Step(2, 20), Step(3, 30), Step(4, 40, 1, 2, 3)], 2);
        var repeated = RoutingGraph.Validate(Routing, [Step(1, 10), Step(2, 20), Step(3, 30), Step(4, 40, 1, 1, 1)], 2);

        many.Should().ContainSingle().Which.Kind.Should().Be(RoutingGraphIssueKind.TooManyPredecessors);
        repeated.Should().BeEmpty();
    }

    [Fact]
    public void The_version_ignores_input_order_and_changes_with_numbers_links_and_gates()
    {
        var steps = new List<RoutingStep> { new(1, 10), new(2, 20), new(3, 30) };
        var links = new List<RoutingLink> { new(3, 1, PredecessorGate.Quantity), new(2, 1, PredecessorGate.Quantity) };
        var version = RoutingGraph.Version(steps, links);

        RoutingGraph.Version([.. steps.AsEnumerable().Reverse()], [.. links.AsEnumerable().Reverse()]).Should().Be(version);
        RoutingGraph.Version([new(1, 10), new(2, 20), new(3, 40)], links).Should().NotBe(version);
        RoutingGraph.Version(steps, [links[0]]).Should().NotBe(version);
        RoutingGraph.Version(steps, [links[0], links[1] with { Gate = PredecessorGate.Complete }]).Should().NotBe(version);
        version.Should().HaveLength(16);
    }
}
