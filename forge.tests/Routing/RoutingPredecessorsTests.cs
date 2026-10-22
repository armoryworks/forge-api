using FluentAssertions;

using Forge.Core.Enums;
using Forge.Core.Routing;

namespace Forge.Tests.Routing;

public class RoutingPredecessorsTests
{
    private static readonly List<RoutingStep> Steps = [new(1, 10), new(2, 20), new(3, 30), new(4, 40)];

    private static RoutingLink Link(int op, int predecessor, PredecessorGate gate = PredecessorGate.Quantity)
        => new(op, predecessor, gate);

    [Fact]
    public void A_step_with_no_links_follows_the_previous_step_number()
    {
        var effective = RoutingPredecessors.Effective(Steps, []);

        effective[1].Should().BeEmpty("the first step follows nothing");
        effective[2].Should().Equal(Link(2, 1));
        effective[3].Should().Equal(Link(3, 2));
        effective[4].Should().Equal(Link(4, 3));
    }

    [Fact]
    public void Steps_sharing_the_previous_step_number_are_all_followed()
    {
        var effective = RoutingPredecessors.Effective([new(1, 10), new(2, 20), new(3, 20), new(4, 30)], []);

        effective[4].Select(l => l.PredecessorOperationId).Should().BeEquivalentTo([2, 3]);
    }

    [Fact]
    public void Parallel_steps_list_the_same_predecessor_and_a_join_lists_both()
    {
        var effective = RoutingPredecessors.Effective(Steps, [Link(2, 1), Link(3, 1), Link(4, 2), Link(4, 3, PredecessorGate.Complete)]);

        effective[2].Should().Equal(Link(2, 1));
        effective[3].Should().Equal(new[] { Link(3, 1) }, "step 30 runs alongside step 20 instead of after it");
        effective[4].Should().BeEquivalentTo([Link(4, 2), Link(4, 3, PredecessorGate.Complete)]);
    }

    [Fact]
    public void Links_to_a_higher_step_to_itself_or_outside_the_routing_are_conflicts_and_ignored()
    {
        List<RoutingLink> links = [Link(2, 3), Link(3, 3), Link(4, 99), Link(4, 1)];

        RoutingPredecessors.Conflicts(Steps, links).Should().BeEquivalentTo([Link(2, 3), Link(3, 3), Link(4, 99)]);

        var effective = RoutingPredecessors.Effective(Steps, links);
        effective[2].Should().Equal(new[] { Link(2, 1) }, "an invalid link falls back to the previous step");
        effective[3].Should().Equal(Link(3, 2));
        effective[4].Should().Equal(Link(4, 1));
    }

    [Fact]
    public void A_link_to_a_step_with_the_same_number_is_a_conflict()
    {
        RoutingPredecessors.Conflicts([new(1, 10), new(2, 10)], [Link(2, 1)]).Should().ContainSingle();
    }
}
