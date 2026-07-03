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

    // Navigation
    public ICollection<EventAttendee> Attendees { get; set; } = new List<EventAttendee>();
}
