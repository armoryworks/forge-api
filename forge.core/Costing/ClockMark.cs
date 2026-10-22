namespace Forge.Core.Costing;

/// <summary>One clock event reduced to its timestamp and the attendance state it starts.</summary>
public sealed record ClockMark(DateTimeOffset At, ClockState State);
