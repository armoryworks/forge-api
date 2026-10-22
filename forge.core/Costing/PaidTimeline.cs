namespace Forge.Core.Costing;

/// <summary>Turns a person's clock events into the spans they are paid for.</summary>
public static class PaidTimeline
{
    /// <summary>
    /// The paid spans inside <paramref name="windowStart"/>–<paramref name="windowEnd"/>. Time in the In state is
    /// paid; break time is paid only when <paramref name="breaksPauseLabor"/> is false; each stretch counts for at
    /// most <paramref name="maxStretch"/>, so a missed clock-out does not pay forever. Returns null (no cap: the
    /// whole window is paid) when <paramref name="usesClock"/> is false, i.e. the person has no clock events on
    /// the days the window covers.
    /// </summary>
    public static IReadOnlyList<PaidSpan>? Build(
        IEnumerable<ClockMark> marks,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        bool breaksPauseLabor,
        TimeSpan maxStretch,
        bool usesClock)
    {
        if (!usesClock)
            return null;

        var ordered = marks.OrderBy(m => m.At).ToList();
        var spans = new List<PaidSpan>();
        for (var i = 0; i < ordered.Count; i++)
        {
            var mark = ordered[i];
            var paid = mark.State == ClockState.In || (mark.State == ClockState.Break && !breaksPauseLabor);
            if (!paid)
                continue;

            var stretchStart = mark.At;
            var j = i + 1;
            while (j < ordered.Count && IsPaid(ordered[j].State, breaksPauseLabor))
                j++;
            var stretchEnd = j < ordered.Count ? ordered[j].At : windowEnd;
            var capped = stretchStart + maxStretch;
            if (stretchEnd > capped)
                stretchEnd = capped;

            var start = stretchStart < windowStart ? windowStart : stretchStart;
            var end = stretchEnd > windowEnd ? windowEnd : stretchEnd;
            if (end > start)
                spans.Add(new PaidSpan(start, end));
            i = j - 1;
        }

        return spans;
    }

    private static bool IsPaid(ClockState state, bool breaksPauseLabor)
        => state == ClockState.In || (state == ClockState.Break && !breaksPauseLabor);
}
