using Forge.Core.Enums;
using Forge.Core.Entities.Calendar;

namespace Forge.Core.Entities;

public class Event : BaseAuditableEntity
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
    public string? Location { get; set; }

    /// <summary>
    /// Legacy fixed classification. Being superseded by <see cref="EventTypeId"/> (the
    /// configurable taxonomy, compliance-calendar A-1). Kept during expand/contract;
    /// dropped in Stage 7 once all reads use the FK.
    /// </summary>
    public EventType EventType { get; set; }

    /// <summary>
    /// FK into the configurable calendar taxonomy (compliance-calendar A-1). Nullable
    /// during the expand phase; backfilled from <see cref="EventType"/> at seed time and
    /// made required in Stage 7.
    /// </summary>
    public int? EventTypeId { get; set; }
    public CalendarEventType? CalendarEventType { get; set; }

    public bool IsRequired { get; set; }
    public int CreatedByUserId { get; set; }
    public bool IsCancelled { get; set; }
    public DateTimeOffset? ReminderSentAt { get; set; }
    public bool IsAllDay { get; set; }
    public bool IsSystemGenerated { get; set; }

    // ── compliance-calendar A-4: tiered workflow (populated only when the event's type
    //    is flagged RequiresTracking; reminder-tier events leave these null/default) ──

    /// <summary>Workflow status; null for reminder-tier events. Overdue is derived.</summary>
    public EventStatus? Status { get; set; }
    public int? OwnerUserId { get; set; }
    public int? CompletedByUserId { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? WaivedReason { get; set; }

    /// <summary>Forced-acknowledgement alert — blocking, must-ack (A-4 escalation).</summary>
    public bool IsBlocking { get; set; }
    public int? AcknowledgedByUserId { get; set; }
    public DateTimeOffset? AcknowledgedAt { get; set; }

    /// <summary>Evidence: a stored DocumentSet and/or an external URL.</summary>
    public int? EvidenceDocumentSetId { get; set; }
    public string? EvidenceUrl { get; set; }

    /// <summary>compliance-calendar A-5: RFC-5545 RRULE for recurring events (null = one-off).</summary>
    public string? RecurrenceRule { get; set; }

    // Navigation
    public ICollection<EventAttendee> Attendees { get; set; } = new List<EventAttendee>();
}
