using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class EventAttendee : BaseEntity
{
    public int EventId { get; set; }
    public int UserId { get; set; }
    public AttendeeStatus Status { get; set; } = AttendeeStatus.Invited;
    public DateTimeOffset? RespondedAt { get; set; }

    // Navigation
    public Event Event { get; set; } = null!;
}
