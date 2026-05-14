
namespace Forge.Core.Entities;

public class Event : BaseAuditableEntity
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
    public string? Location { get; set; }
    public EventType EventType { get; set; }
    public bool IsRequired { get; set; }
    public int CreatedByUserId { get; set; }
    public bool IsCancelled { get; set; }
    public DateTimeOffset? ReminderSentAt { get; set; }
    public bool IsAllDay { get; set; }
    public bool IsSystemGenerated { get; set; }

    // Navigation
    public ICollection<EventAttendee> Attendees { get; set; } = new List<EventAttendee>();
}
