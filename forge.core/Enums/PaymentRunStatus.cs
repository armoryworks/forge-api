namespace Forge.Core.Enums;

/// <summary>
/// Lifecycle of a payment run. A run is Pending until it starts, Running while it pays vendors, Completed when every
/// vendor is paid, and Interrupted when a vendor failed part-way: the vendors already paid stay paid and resuming pays
/// only the rest. Cancelled closes an interrupted run without paying the rest. Stored as an integer.
/// </summary>
public enum PaymentRunStatus
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    Interrupted = 3,
    Cancelled = 4,
}
