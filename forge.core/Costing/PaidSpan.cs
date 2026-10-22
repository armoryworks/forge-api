namespace Forge.Core.Costing;

/// <summary>A stretch of time the person is paid for (clocked in and, when breaks pause labor, not on a break).</summary>
public sealed record PaidSpan(DateTimeOffset Start, DateTimeOffset End);
