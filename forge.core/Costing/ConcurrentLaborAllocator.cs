namespace Forge.Core.Costing;

/// <summary>
/// Shares a person's paid time across the timers they had open at the same moment. The time between any two
/// timer, clock or break edges is one segment; its paid time goes to the timers open in it in proportion to their
/// attendance weight (an even split when every weight is equal, an equal split when every weight is 0). Shares are
/// computed exactly, then rounded to 0.01 minute by largest remainder within each group of overlapping timers, so
/// the rounded shares of a group add up to its paid minutes.
/// </summary>
public static class ConcurrentLaborAllocator
{
    private const decimal TicksPerCentiminute = TimeSpan.TicksPerMinute / 100m;

    /// <summary>
    /// Allocates <paramref name="timers"/> against <paramref name="paid"/> (null = every moment is paid) and returns
    /// the shares of the timers whose ids are in <paramref name="targets"/> (null = all of them). Timers outside the
    /// targets still take their share of each segment; they are just not returned or rounded.
    /// </summary>
    public static IReadOnlyList<LaborAllocation> Allocate(
        IReadOnlyList<LaborTimer> timers,
        IReadOnlyList<PaidSpan>? paid,
        IReadOnlySet<int>? targets = null)
    {
        var valid = timers.Where(t => t.Stop > t.Start).ToList();
        if (valid.Count == 0)
            return [];

        var cuts = new SortedSet<DateTimeOffset>();
        foreach (var timer in valid)
        {
            cuts.Add(timer.Start);
            cuts.Add(timer.Stop);
        }

        var first = valid.Min(t => t.Start);
        var last = valid.Max(t => t.Stop);
        if (paid is not null)
        {
            foreach (var span in paid)
            {
                if (span.Start > first && span.Start < last) cuts.Add(span.Start);
                if (span.End > first && span.End < last) cuts.Add(span.End);
            }
        }

        var exactTicks = valid.ToDictionary(t => t.Id, _ => 0m);
        var shared = valid.ToDictionary(t => t.Id, _ => false);
        var points = cuts.ToList();
        for (var i = 0; i + 1 < points.Count; i++)
        {
            var from = points[i];
            var to = points[i + 1];
            var open = valid.Where(t => t.Start <= from && t.Stop >= to).ToList();
            if (open.Count == 0)
                continue;

            var length = (decimal)(to - from).Ticks;
            var paidTicks = paid is null || paid.Any(s => s.Start <= from && s.End >= to) ? length : 0m;
            var totalWeight = open.Sum(t => Math.Max(t.Weight, 0m));
            foreach (var timer in open)
            {
                var weight = totalWeight == 0m ? 1m : Math.Max(timer.Weight, 0m);
                var weightSum = totalWeight == 0m ? open.Count : totalWeight;
                exactTicks[timer.Id] += paidTicks * weight / weightSum;
                if (open.Count > 1 || paidTicks < length)
                    shared[timer.Id] = true;
            }
        }

        var results = new List<LaborAllocation>();
        foreach (var cluster in Clusters(valid))
        {
            var members = cluster.Where(t => targets is null || targets.Contains(t.Id)).ToList();
            if (members.Count == 0)
                continue;

            var exact = members.ToDictionary(t => t.Id, t => exactTicks[t.Id] / TicksPerCentiminute);
            var floors = exact.ToDictionary(e => e.Key, e => Math.Floor(e.Value));
            var units = (int)(Math.Round(exact.Values.Sum(), MidpointRounding.AwayFromZero) - floors.Values.Sum());
            var bumped = members
                .OrderByDescending(t => exact[t.Id] - floors[t.Id])
                .ThenBy(t => t.Start)
                .ThenBy(t => t.Id)
                .Take(Math.Max(units, 0))
                .Select(t => t.Id)
                .ToHashSet();

            foreach (var timer in members)
            {
                var centiminutes = floors[timer.Id] + (bumped.Contains(timer.Id) ? 1m : 0m);
                results.Add(new LaborAllocation(
                    timer.Id,
                    centiminutes / 100m,
                    Math.Round((decimal)(timer.Stop - timer.Start).Ticks / TimeSpan.TicksPerMinute, 2, MidpointRounding.AwayFromZero),
                    shared[timer.Id]));
            }
        }

        return results.OrderBy(r => r.TimerId).ToList();
    }

    private static IEnumerable<List<LaborTimer>> Clusters(List<LaborTimer> timers)
    {
        var ordered = timers.OrderBy(t => t.Start).ThenBy(t => t.Id).ToList();
        var current = new List<LaborTimer>();
        var reach = DateTimeOffset.MinValue;
        foreach (var timer in ordered)
        {
            if (current.Count > 0 && timer.Start >= reach)
            {
                yield return current;
                current = [];
            }
            current.Add(timer);
            if (timer.Stop > reach)
                reach = timer.Stop;
        }
        if (current.Count > 0)
            yield return current;
    }
}
