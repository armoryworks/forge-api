using FluentAssertions;

using Forge.Core.Costing;

namespace Forge.Tests.Costing;

public class MachineTimeSharingTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 7, 13, 0, 0, TimeSpan.Zero);

    private static MachineInterval Span(double start, double? stop)
        => new(T0.AddMinutes(start), stop is double s ? T0.AddMinutes(s) : null);

    [Fact]
    public void A_timer_with_no_overlap_carries_all_of_its_time()
    {
        MachineTimeSharing.ShareOf(Span(0, 60), [Span(60, 90), Span(-30, 0)]).Should().Be(1m);
    }

    [Fact]
    public void Two_operators_on_the_same_cycle_share_it_evenly()
    {
        MachineTimeSharing.ShareOf(Span(0, 60), [Span(0, 60)]).Should().Be(0.5m);
    }

    [Fact]
    public void Staggered_timers_add_up_to_the_union_of_their_intervals()
    {
        var a = Span(0, 60);
        var b = Span(30, 90);
        var c = Span(45, 50);

        var minutes = MachineTimeSharing.ShareOf(a, [b, c]) * 60m
            + MachineTimeSharing.ShareOf(b, [a, c]) * 60m
            + MachineTimeSharing.ShareOf(c, [a, b]) * 5m;

        minutes.Should().BeApproximately(90m, 0.000001m, "the machine ran from 0 to 90 once");
    }

    [Fact]
    public void A_share_written_at_stop_does_not_move_when_a_running_sibling_stops_later()
    {
        var first = Span(0, 60);
        var atStop = MachineTimeSharing.ShareOf(first, [Span(30, null)]);
        var afterSiblingStops = MachineTimeSharing.ShareOf(first, [Span(30, 120)]);

        atStop.Should().Be(afterSiblingStops);
        (atStop * 60m + MachineTimeSharing.ShareOf(Span(30, 120), [first]) * 90m).Should().BeApproximately(120m, 0.000001m);
    }

    [Fact]
    public void A_timer_with_no_length_keeps_a_full_share()
    {
        MachineTimeSharing.ShareOf(Span(10, 10), [Span(0, 60)]).Should().Be(1m);
        MachineTimeSharing.ShareOf(Span(10, null), [Span(0, 60)]).Should().Be(1m);
    }
}
