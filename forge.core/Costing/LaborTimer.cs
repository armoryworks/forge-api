namespace Forge.Core.Costing;

/// <summary>One timer interval fed to <see cref="ConcurrentLaborAllocator"/>. <paramref name="Weight"/> is the
/// operator attendance (0–100): 100 for attended work, lower for steps where a machine runs the cycle.</summary>
public sealed record LaborTimer(int Id, DateTimeOffset Start, DateTimeOffset Stop, decimal Weight);
