using FluentAssertions;

using Forge.Core.Sequences;

using static Forge.Tests.Sequences.SequenceTestNets;

namespace Forge.Tests.Sequences;

public class SequenceNetValidatorTests
{
    [Fact]
    public void Accepts_a_serial_and_a_fork_join_net()
    {
        SequenceNetValidator.Validate(Serial()).Should().BeEmpty();
        SequenceNetValidator.Validate(ForkJoin()).Should().BeEmpty();
    }

    [Fact]
    public void Rejects_duplicate_step_keys_and_dangling_edges()
    {
        var d = Definition("x", "a", "a").Edge("a", "zzz");
        var errors = SequenceNetValidator.Validate(d);
        errors.Should().Contain(e => e.Contains("Duplicate step key 'a'"));
        errors.Should().Contain(e => e.Contains("unknown step 'zzz'"));
    }

    [Fact]
    public void Rejects_a_cycle_unless_it_is_a_rework_edge()
    {
        var cyclic = Definition("c", "a", "b").Edge("a", "b").Edge("b", "a");
        SequenceNetValidator.Validate(cyclic).Should().Contain(e => e.Contains("cycle") || e.Contains("No start step"));

        var rework = Definition("r", "a", "b").Edge("a", "b").Edge("b", "a", rework: true);
        SequenceNetValidator.Validate(rework).Should().BeEmpty();
    }

    [Fact]
    public void Rejects_gates_on_unknown_steps()
    {
        var d = Definition("g", "a", "b").Edge("a", "b").Gate("nope", "g");
        SequenceNetValidator.Validate(d).Should().Contain(e => e.Contains("unknown step 'nope'"));
    }

    [Fact]
    public void A_step_whose_only_incoming_edge_is_rework_is_a_start_step()
    {
        // rework edges never gate readiness, so 'island' has no real predecessor and starts immediately — valid.
        var d = Definition("u", "a", "b", "island").Edge("a", "b").Edge("b", "island", rework: true);
        SequenceNetValidator.Validate(d).Should().BeEmpty();
        new SequenceNet(d).StartSteps().Select(s => s.Key).Should().BeEquivalentTo(["a", "island"]);
    }

    [Fact]
    public void A_closed_cycle_off_the_main_path_is_unreachable_and_reported()
    {
        // a → b is fine; c ⇄ d has no entry point → both a cycle and unreachable steps.
        var d = Definition("cyc", "a", "b", "c", "d").Edge("a", "b").Edge("c", "d").Edge("d", "c");
        var errors = SequenceNetValidator.Validate(d);
        errors.Should().Contain(e => e.Contains("'c' is unreachable"));
        errors.Should().Contain(e => e.Contains("'d' is unreachable"));
        errors.Should().Contain(e => e.Contains("cycle"));
    }
}
