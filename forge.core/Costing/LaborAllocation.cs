namespace Forge.Core.Costing;

/// <summary>The labor share of one timer. <paramref name="LaborMinutes"/> is rounded to 0.01 minute so the shares
/// of a group of overlapping timers add up exactly to the paid minutes; <paramref name="Shared"/> is false when
/// the timer ran alone and fully paid, i.e. its labor is simply its elapsed time.</summary>
public sealed record LaborAllocation(int TimerId, decimal LaborMinutes, decimal ElapsedMinutes, bool Shared);
