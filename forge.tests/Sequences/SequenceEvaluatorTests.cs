using FluentAssertions;

using Forge.Core.Enums;
using Forge.Core.Sequences;

using static Forge.Tests.Sequences.SequenceTestNets;

namespace Forge.Tests.Sequences;

public class SequenceEvaluatorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Start_steps_become_ready_and_the_rest_wait()
    {
        var d = Serial(); var i = Instance(d);
        var r = Eval(d, i, now: T0);

        i.Step("a").Status.Should().Be(SequenceStepStatus.Ready);
        i.Step("a").ReadyAt.Should().Be(T0);
        i.Step("b").Status.Should().Be(SequenceStepStatus.Pending);
        r.NewlyReady.Should().Equal("a");
        r.Events.Should().ContainSingle(e => e.Type == SequenceEventType.StepReady && e.StepKey == "a");
    }

    [Fact]
    public void Evaluation_is_idempotent()
    {
        var d = Serial(); var i = Instance(d);
        Eval(d, i, now: T0);
        var second = Eval(d, i, now: T0.AddMinutes(1));
        second.Changed.Should().BeFalse();
        second.Events.Should().BeEmpty();
    }

    [Fact]
    public void A_gate_that_is_not_go_blocks_the_step_and_reports_why()
    {
        var d = Serial(); var i = Instance(d);
        i.Step("a").Status = SequenceStepStatus.Complete;
        var r = Eval(d, i, Verdicts((("b", "inspect"), SequenceGateVerdictResult.NoGo("Awaiting clearance"))), T0);

        i.Step("b").Status.Should().Be(SequenceStepStatus.Pending);
        r.Blocked.Should().Equal("b");
        SequenceEvaluator.IsBlocked(new SequenceNet(d), i, "b").Should().BeTrue();
        i.Gate("b", "inspect").Verdict.Should().Be(SequenceGateVerdict.NoGo);
        i.Gate("b", "inspect").Reason.Should().Be("Awaiting clearance");
    }

    [Fact]
    public void Gate_go_makes_the_step_ready_and_a_later_no_go_returns_it_to_pending()
    {
        var d = Serial(); var i = Instance(d);
        i.Step("a").Status = SequenceStepStatus.Complete;
        Eval(d, i, Verdicts((("b", "inspect"), SequenceGateVerdictResult.Go())), T0);
        i.Step("b").Status.Should().Be(SequenceStepStatus.Ready);

        var r = Eval(d, i, Verdicts((("b", "inspect"), SequenceGateVerdictResult.NoGo("expired"))), T0.AddHours(1));
        i.Step("b").Status.Should().Be(SequenceStepStatus.Pending);
        r.Events.Should().Contain(e => e.Type == SequenceEventType.StepBlocked && e.StepKey == "b");
    }

    [Fact]
    public void An_overridden_gate_stays_go_whatever_the_source_says()
    {
        var d = Serial(); var i = Instance(d);
        i.Step("a").Status = SequenceStepStatus.Complete;
        i.Gate("b", "inspect").OverriddenAt = T0; i.Gate("b", "inspect").OverrideReason = "supervisor waived";
        Eval(d, i, Verdicts((("b", "inspect"), SequenceGateVerdictResult.NoGo("Awaiting clearance"))), T0);

        i.Gate("b", "inspect").Verdict.Should().Be(SequenceGateVerdict.Go);
        i.Gate("b", "inspect").Reason.Should().StartWith("Overridden:");
        i.Step("b").Status.Should().Be(SequenceStepStatus.Ready);
    }

    [Fact]
    public void Join_all_waits_for_every_predecessor_and_join_any_for_one()
    {
        var d = ForkJoin(); var i = Instance(d);
        i.Step("prep1").Status = SequenceStepStatus.Complete;
        Eval(d, i, now: T0);
        i.Step("assemble").Status.Should().Be(SequenceStepStatus.Pending, "prep2 is not done");

        i.Step("prep2").Status = SequenceStepStatus.Skipped; // skipped counts as done
        Eval(d, i, now: T0);
        i.Step("assemble").Status.Should().Be(SequenceStepStatus.Ready);

        var any = ForkJoin(); any.Steps.First(s => s.Key == "assemble").JoinPolicy = SequenceJoinPolicy.Any;
        var j = Instance(any);
        j.Step("prep1").Status = SequenceStepStatus.Complete;
        Eval(any, j, now: T0);
        j.Step("assemble").Status.Should().Be(SequenceStepStatus.Ready, "join policy Any");
    }

    [Fact]
    public void Instance_completes_when_every_step_is_complete_or_skipped()
    {
        var d = Serial(); var i = Instance(d);
        foreach (var s in i.Steps) s.Status = SequenceStepStatus.Complete;
        i.Step("c").Status = SequenceStepStatus.Skipped;
        var r = Eval(d, i, now: T0);

        r.CompletedInstance.Should().BeTrue();
        i.Status.Should().Be(SequenceInstanceStatus.Completed);
        i.CompletedAt.Should().Be(T0);
        r.Events.Should().ContainSingle(e => e.Type == SequenceEventType.InstanceCompleted);
    }

    [Fact]
    public void Downstream_of_a_step_follows_only_non_rework_edges()
    {
        var d = ForkJoin().Edge("ship", "prep1", rework: true);
        var net = new SequenceNet(d);
        net.Downstream("prep1").Should().BeEquivalentTo(["assemble", "ship"]);
        net.Downstream("ship").Should().BeEmpty();
        net.StartSteps().Select(s => s.Key).Should().BeEquivalentTo(["prep1", "prep2"]);
    }
}
