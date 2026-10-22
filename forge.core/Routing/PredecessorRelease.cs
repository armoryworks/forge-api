namespace Forge.Core.Routing;

/// <summary>
/// When a routing step may start, given where its predecessors were scheduled. A step starts after every
/// predecessor ends (finish-to-start), so steps that follow the same step can start together. A predecessor with
/// an overlap percent lets its follower start that share of its duration before it ends, but the follower may not
/// end before it does. <see cref="Finish"/> is the latest predecessor finish, ignoring overlap.
/// </summary>
public sealed record PredecessorRelease(DateTimeOffset? NotBefore, DateTimeOffset? MustNotEndBefore, DateTimeOffset? Finish)
{
    public static PredecessorRelease From(IReadOnlyList<ScheduledSpan> predecessors)
    {
        DateTimeOffset? notBefore = null;
        DateTimeOffset? mustNotEndBefore = null;
        DateTimeOffset? finish = null;

        foreach (var span in predecessors)
        {
            var spanFinish = span.Floor is { } floor && floor > span.End ? floor : span.End;
            finish = Later(finish, spanFinish);

            DateTimeOffset release;
            if (span.OverlapPercent > 0 && spanFinish == span.End)
            {
                var fraction = Math.Min(span.OverlapPercent, 100m) / 100m;
                var overlapStart = span.End.AddHours(-(span.End - span.Start).TotalHours * (double)fraction);
                release = overlapStart > span.Start ? overlapStart : span.Start;
                mustNotEndBefore = Later(mustNotEndBefore, span.End);
            }
            else
            {
                release = spanFinish;
            }

            notBefore = Later(notBefore, release);
        }

        return new PredecessorRelease(notBefore, mustNotEndBefore, finish);
    }

    private static DateTimeOffset Later(DateTimeOffset? current, DateTimeOffset candidate)
        => current is { } c && c >= candidate ? c : candidate;
}
