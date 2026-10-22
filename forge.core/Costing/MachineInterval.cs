namespace Forge.Core.Costing;

/// <summary>One timer on a machine-basis step. A null <paramref name="Stop"/> is a timer still running.</summary>
public sealed record MachineInterval(DateTimeOffset Start, DateTimeOffset? Stop);
