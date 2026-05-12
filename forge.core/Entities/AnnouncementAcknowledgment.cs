namespace Forge.Core.Entities;

public class AnnouncementAcknowledgment : BaseEntity
{
    public int AnnouncementId { get; set; }
    public int UserId { get; set; }
    public DateTimeOffset AcknowledgedAt { get; set; }

    public Announcement Announcement { get; set; } = null!;
}
