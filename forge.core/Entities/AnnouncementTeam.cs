namespace Forge.Core.Entities;

public class AnnouncementTeam
{
    public int AnnouncementId { get; set; }
    public int TeamId { get; set; }

    public Announcement Announcement { get; set; } = null!;
    public Team Team { get; set; } = null!;
}
