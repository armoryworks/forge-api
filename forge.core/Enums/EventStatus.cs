namespace Forge.Core.Enums;

/// <summary>
/// compliance-calendar A-4: workflow status for tracking-tier events (event types flagged
/// RequiresTracking). Reminder-tier events leave this null. Overdue is derived, not stored.
/// </summary>
public enum EventStatus
{
    Open,
    InProgress,
    Done,
    Waived,
}
