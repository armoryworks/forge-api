using FluentAssertions;

using Forge.Core.Enums;
using Forge.Core.Routing;

namespace Forge.Tests.Routing;

public class StepGateTests
{
    private static IReadOnlyDictionary<int, IReadOnlyList<RoutingLink>> Graph(params (int Op, int[] Predecessors)[] steps)
        => steps.ToDictionary(
            s => s.Op,
            s => (IReadOnlyList<RoutingLink>)s.Predecessors.Select(p => new RoutingLink(s.Op, p, PredecessorGate.Quantity)).ToList());

    private static Dictionary<int, StepProgress> Progress(params (int Op, JobOperationStatus Status, decimal Completed)[] rows)
        => rows.ToDictionary(r => r.Op, r => new StepProgress(r.Status, r.Completed));

    [Fact]
    public void A_step_with_no_predecessors_has_no_limit()
    {
        StepGate.Available(1, Graph((1, [])), Progress(), 20m).Should().Be(new StepAvailability(null, null));
    }

    [Fact]
    public void Available_is_the_predecessors_good_count()
    {
        var available = StepGate.Available(2, Graph((1, []), (2, [1])), Progress((1, JobOperationStatus.InProgress, 12m)), 20m);

        available.Should().Be(new StepAvailability(12m, 1));
    }

    [Fact]
    public void A_predecessor_that_has_not_started_makes_nothing_available()
    {
        StepGate.Available(2, Graph((1, []), (2, [1])), Progress(), 20m).Should().Be(new StepAvailability(0m, 1));
    }

    [Fact]
    public void A_join_takes_the_fewest_pieces_of_its_predecessors()
    {
        var available = StepGate.Available(
            4,
            Graph((1, []), (2, [1]), (3, [1]), (4, [2, 3])),
            Progress((1, JobOperationStatus.Complete, 20m), (2, JobOperationStatus.InProgress, 15m), (3, JobOperationStatus.InProgress, 9m)),
            20m);

        available.Should().Be(new StepAvailability(9m, 3));
    }

    [Fact]
    public void A_skipped_predecessor_passes_its_own_predecessors_through()
    {
        var available = StepGate.Available(
            3,
            Graph((1, []), (2, [1]), (3, [2])),
            Progress((1, JobOperationStatus.InProgress, 7m), (2, JobOperationStatus.Skipped, 0m)),
            20m);

        available.Should().Be(new StepAvailability(7m, 1));
    }

    [Fact]
    public void A_skipped_first_step_passes_the_job_quantity()
    {
        var available = StepGate.Available(2, Graph((1, []), (2, [1])), Progress((1, JobOperationStatus.Skipped, 0m)), 20m);

        available.Available.Should().Be(20m);
    }

    [Fact]
    public void A_complete_predecessor_with_no_count_counts_as_the_job_quantity()
    {
        var available = StepGate.Available(2, Graph((1, []), (2, [1])), Progress((1, JobOperationStatus.Complete, 0m)), 20m);

        available.Should().Be(new StepAvailability(20m, 1));
    }

    [Fact]
    public void Complete_gated_predecessors_that_are_not_done_are_waited_on()
    {
        var graph = new Dictionary<int, IReadOnlyList<RoutingLink>>
        {
            [3] = [new(3, 1, PredecessorGate.Complete), new(3, 2, PredecessorGate.Quantity)],
        };

        StepGate.WaitingOn(3, graph, Progress((1, JobOperationStatus.InProgress, 5m), (2, JobOperationStatus.InProgress, 5m)))
            .Should().Equal(1);
        StepGate.WaitingOn(3, graph, Progress((1, JobOperationStatus.Complete, 5m))).Should().BeEmpty();
        StepGate.WaitingOn(3, graph, Progress((1, JobOperationStatus.Skipped, 0m))).Should().BeEmpty("a skipped step counts as done");
    }
}
