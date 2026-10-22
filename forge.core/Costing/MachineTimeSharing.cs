namespace Forge.Core.Costing;

/// <summary>
/// Shares a machine's elapsed time across the timers that ran on the same step at once, so overlapping operators
/// burden the machine once. Each moment of the target timer is divided by the number of timers open at that
/// moment, so the shares of a set of overlapping timers add up to the union of their intervals. A timer that is
/// still running counts as open to the end of the target, which keeps a share stable once it is written.
/// </summary>
public static class MachineTimeSharing
{
    /// <summary>
    /// The fraction (0–1] of <paramref name="target"/>'s elapsed time it carries once <paramref name="others"/>
    /// take their share. Returns 1 when nothing overlaps or the target has no length.
    /// </summary>
    public static decimal ShareOf(MachineInterval target, IEnumerable<MachineInterval> others)
    {
        if (target.Stop is not DateTimeOffset end || end <= target.Start)
            return 1m;

        var overlapping = others
            .Select(o => (Start: o.Start > target.Start ? o.Start : target.Start,
                End: o.Stop is DateTimeOffset stop && stop < end ? stop : end))
            .Where(o => o.End > o.Start)
            .ToList();
        if (overlapping.Count == 0)
            return 1m;

        var cuts = new SortedSet<DateTimeOffset> { target.Start, end };
        foreach (var span in overlapping)
        {
            cuts.Add(span.Start);
            cuts.Add(span.End);
        }

        var points = cuts.ToList();
        var shared = 0m;
        for (var i = 0; i < points.Count - 1; i++)
        {
            var from = points[i];
            var to = points[i + 1];
            var open = 1 + overlapping.Count(o => o.Start <= from && o.End >= to);
            shared += (decimal)(to - from).Ticks / open;
        }

        return shared / (end - target.Start).Ticks;
    }
}
