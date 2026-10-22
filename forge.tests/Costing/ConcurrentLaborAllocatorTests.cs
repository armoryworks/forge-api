using FluentAssertions;

using Forge.Core.Costing;

namespace Forge.Tests.Costing;

public class ConcurrentLaborAllocatorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 7, 13, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset At(double minutes) => T0.AddMinutes(minutes);

    private static LaborTimer Timer(int id, double start, double stop, decimal weight = 100m)
        => new(id, At(start), At(stop), weight);

    private static Dictionary<int, decimal> Labor(IReadOnlyList<LaborAllocation> results)
        => results.ToDictionary(r => r.TimerId, r => r.LaborMinutes);

    [Fact]
    public void One_timer_gets_its_full_duration_and_is_not_shared()
    {
        var result = ConcurrentLaborAllocator.Allocate([Timer(1, 0, 60)], paid: null).Single();

        result.LaborMinutes.Should().Be(60m);
        result.ElapsedMinutes.Should().Be(60m);
        result.Shared.Should().BeFalse();
    }

    [Fact]
    public void Three_fully_attended_timers_split_evenly()
    {
        var labor = Labor(ConcurrentLaborAllocator.Allocate(
            [Timer(1, 0, 35), Timer(2, 0, 35), Timer(3, 0, 35)], paid: null));

        labor.Values.Should().AllSatisfy(v => v.Should().BeApproximately(11.67m, 0.01m));
        labor.Values.Sum().Should().Be(35m);
    }

    [Fact]
    public void Attendance_weights_split_a_35_minute_window_20_100_100()
    {
        var labor = Labor(ConcurrentLaborAllocator.Allocate(
            [Timer(2, 0, 35, 20m), Timer(8, 0, 35), Timer(4, 0, 35)], paid: null));

        labor[2].Should().Be(3.18m);
        labor[8].Should().Be(15.91m);
        labor[4].Should().Be(15.91m);
        labor.Values.Sum().Should().Be(35.00m);
    }

    [Fact]
    public void Every_timer_unattended_falls_back_to_an_equal_split()
    {
        var labor = Labor(ConcurrentLaborAllocator.Allocate(
            [Timer(1, 0, 30, 0m), Timer(2, 0, 30, 0m)], paid: null));

        labor[1].Should().Be(15m);
        labor[2].Should().Be(15m);
    }

    [Fact]
    public void Staggered_starts_and_stops_share_only_the_overlap()
    {
        var labor = Labor(ConcurrentLaborAllocator.Allocate(
            [Timer(1, 0, 60), Timer(2, 30, 90)], paid: null));

        labor[1].Should().Be(45m);
        labor[2].Should().Be(45m);
        labor.Values.Sum().Should().Be(90m);
    }

    [Theory]
    [InlineData(true, 25, 25)]
    [InlineData(false, 30, 30)]
    public void A_break_inside_the_window_pays_nothing_when_breaks_pause_labor(bool breaksPause, double expectedA, double expectedB)
    {
        var marks = new List<ClockMark>
        {
            new(At(-60), ClockState.In),
            new(At(20), ClockState.Break),
            new(At(30), ClockState.In),
        };
        var paid = PaidTimeline.Build(marks, At(0), At(60), breaksPause, TimeSpan.FromHours(16), usesClock: true);

        var labor = Labor(ConcurrentLaborAllocator.Allocate([Timer(1, 0, 60), Timer(2, 0, 60)], paid));

        labor[1].Should().Be((decimal)expectedA);
        labor[2].Should().Be((decimal)expectedB);
    }

    [Fact]
    public void A_single_timer_through_an_unpaid_break_is_shared_and_loses_the_break()
    {
        var marks = new List<ClockMark>
        {
            new(At(-5), ClockState.In),
            new(At(20), ClockState.Break),
            new(At(30), ClockState.In),
        };
        var paid = PaidTimeline.Build(marks, At(0), At(60), breaksPauseLabor: true, TimeSpan.FromHours(16), usesClock: true);

        var result = ConcurrentLaborAllocator.Allocate([Timer(1, 0, 60)], paid).Single();

        result.LaborMinutes.Should().Be(50m);
        result.Shared.Should().BeTrue();
    }

    [Fact]
    public void A_timer_running_past_clock_out_is_capped_to_clocked_time()
    {
        var marks = new List<ClockMark> { new(At(-10), ClockState.In), new(At(40), ClockState.Out) };
        var paid = PaidTimeline.Build(marks, At(0), At(120), breaksPauseLabor: true, TimeSpan.FromHours(16), usesClock: true);

        var result = ConcurrentLaborAllocator.Allocate([Timer(1, 0, 120)], paid).Single();

        result.LaborMinutes.Should().Be(40m);
        result.ElapsedMinutes.Should().Be(120m);
    }

    [Fact]
    public void No_clock_events_that_day_means_no_cap()
    {
        var paid = PaidTimeline.Build([], At(0), At(120), breaksPauseLabor: true, TimeSpan.FromHours(16), usesClock: false);

        paid.Should().BeNull();
        ConcurrentLaborAllocator.Allocate([Timer(1, 0, 120)], paid).Single().LaborMinutes.Should().Be(120m);
    }

    [Fact]
    public void A_forgotten_clock_out_pays_at_most_the_stretch_cap()
    {
        var marks = new List<ClockMark> { new(At(0), ClockState.In) };
        var paid = PaidTimeline.Build(marks, At(0), At(20 * 60), breaksPauseLabor: true, TimeSpan.FromHours(16), usesClock: true);

        ConcurrentLaborAllocator.Allocate([Timer(1, 0, 20 * 60)], paid).Single().LaborMinutes.Should().Be(16 * 60m);
    }

    [Fact]
    public void A_timer_started_after_the_stop_does_not_change_the_earlier_result()
    {
        var alone = Labor(ConcurrentLaborAllocator.Allocate([Timer(1, 0, 40), Timer(2, 10, 40)], paid: null, new HashSet<int> { 1 }));
        var withLater = Labor(ConcurrentLaborAllocator.Allocate(
            [Timer(1, 0, 40), Timer(2, 10, 40), Timer(3, 50, 80)], paid: null, new HashSet<int> { 1 }));

        withLater[1].Should().Be(alone[1]);
        withLater.Should().NotContainKey(3);
    }

    [Fact]
    public void Non_target_timers_still_take_their_share_but_are_not_returned()
    {
        var results = ConcurrentLaborAllocator.Allocate([Timer(1, 0, 30), Timer(2, 0, 30)], paid: null, new HashSet<int> { 2 });

        results.Should().ContainSingle().Which.LaborMinutes.Should().Be(15m);
    }

    [Fact]
    public void Rounding_gives_the_remainder_to_the_largest_fractions_so_the_sum_is_the_paid_minutes()
    {
        var labor = Labor(ConcurrentLaborAllocator.Allocate(
            [Timer(1, 0, 10), Timer(2, 0, 10), Timer(3, 0, 10)], paid: null));

        labor.Values.Sum().Should().Be(10.00m);
        labor.Values.Should().OnlyContain(v => v == 3.33m || v == 3.34m);
    }

    [Fact]
    public void Shares_are_stored_to_a_hundredth_of_a_minute()
    {
        var labor = Labor(ConcurrentLaborAllocator.Allocate(
            [new LaborTimer(1, T0, T0.AddSeconds(61), 100m), new LaborTimer(2, T0, T0.AddSeconds(61), 100m)], paid: null));

        labor.Values.Should().OnlyContain(v => decimal.Round(v, 2) == v);
        labor.Values.Sum().Should().Be(1.02m);
    }

    [Fact]
    public void Property_the_shares_of_a_fully_targeted_group_always_add_up_to_its_paid_minutes()
    {
        var random = new Random(20261022);
        for (var run = 0; run < 300; run++)
        {
            var count = random.Next(1, 6);
            var timers = Enumerable.Range(1, count)
                .Select(id =>
                {
                    var start = random.Next(0, 240) + random.Next(0, 60) / 60.0;
                    var length = random.Next(1, 180) + random.Next(0, 60) / 60.0;
                    return Timer(id, start, start + length, random.Next(0, 5) * 25);
                })
                .ToList();
            var marks = new List<ClockMark>
            {
                new(At(random.Next(-30, 60)), ClockState.In),
                new(At(random.Next(60, 120)), ClockState.Break),
                new(At(random.Next(120, 150)), ClockState.In),
                new(At(random.Next(200, 600)), ClockState.Out),
            };
            var paid = PaidTimeline.Build(marks, timers.Min(t => t.Start), timers.Max(t => t.Stop),
                breaksPauseLabor: random.Next(2) == 0, TimeSpan.FromHours(16), usesClock: true)!;

            var results = ConcurrentLaborAllocator.Allocate(timers, paid);

            var expectedTicks = PaidWhileAnyOpen(timers, paid);
            var expected = Math.Round(expectedTicks / (TimeSpan.TicksPerMinute / 100m), MidpointRounding.AwayFromZero) / 100m;
            results.Sum(r => r.LaborMinutes).Should().BeApproximately(expected, 0.01m * Clusters(timers),
                $"run {run} should hand out exactly the paid minutes within each overlap group");
            results.Should().OnlyContain(r => r.LaborMinutes >= 0m && r.LaborMinutes <= r.ElapsedMinutes + 0.01m);
        }
    }

    [Fact]
    public void Property_a_group_rounds_to_exactly_its_paid_minutes()
    {
        var random = new Random(7);
        for (var run = 0; run < 200; run++)
        {
            var timers = Enumerable.Range(1, random.Next(2, 5))
                .Select(id => new LaborTimer(id, T0.AddSeconds(random.Next(0, 50)), T0.AddSeconds(3000 + random.Next(0, 900)), random.Next(0, 101)))
                .ToList();

            var results = ConcurrentLaborAllocator.Allocate(timers, paid: null);

            var paidTicks = (decimal)(timers.Max(t => t.Stop) - timers.Min(t => t.Start)).Ticks;
            var expected = Math.Round(paidTicks / (TimeSpan.TicksPerMinute / 100m), MidpointRounding.AwayFromZero) / 100m;
            results.Sum(r => r.LaborMinutes).Should().Be(expected, $"run {run}");
        }
    }

    private static decimal PaidWhileAnyOpen(List<LaborTimer> timers, IReadOnlyList<PaidSpan> paid)
    {
        var total = 0m;
        foreach (var span in paid)
        {
            var cursor = span.Start;
            var points = timers.SelectMany(t => new[] { t.Start, t.Stop })
                .Where(p => p > span.Start && p < span.End)
                .Append(span.End)
                .OrderBy(p => p)
                .ToList();
            foreach (var point in points)
            {
                if (timers.Any(t => t.Start <= cursor && t.Stop >= point) && point > cursor)
                    total += (point - cursor).Ticks;
                cursor = point;
            }
        }
        return total;
    }

    private static int Clusters(List<LaborTimer> timers)
    {
        var count = 0;
        var reach = DateTimeOffset.MinValue;
        foreach (var timer in timers.OrderBy(t => t.Start))
        {
            if (count == 0 || timer.Start >= reach)
                count++;
            if (timer.Stop > reach)
                reach = timer.Stop;
        }
        return count;
    }
}
