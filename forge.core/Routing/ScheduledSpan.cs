namespace Forge.Core.Routing;

/// <summary>
/// Where a routing step landed in a schedule run, as its followers see it. <see cref="Floor"/> is set for a
/// pinned step whose followers can't start before an earlier step finished, even when the pinned step ends sooner.
/// <see cref="OverlapPercent"/> is how much of this step a follower may overlap.
/// </summary>
public sealed record ScheduledSpan(DateTimeOffset Start, DateTimeOffset End, DateTimeOffset? Floor, decimal OverlapPercent);
