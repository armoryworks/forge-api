
namespace Forge.Core.Entities;

public class Announcement : BaseAuditableEntity
{
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public AnnouncementSeverity Severity { get; set; } = AnnouncementSeverity.Info;
    public AnnouncementScope Scope { get; set; } = AnnouncementScope.CompanyWide;
    public bool RequiresAcknowledgment { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public bool IsSystemGenerated { get; set; }
    public string? SystemSource { get; set; }
    public int? TemplateId { get; set; }
    public int? DepartmentId { get; set; }
    public int CreatedById { get; set; }

    public AnnouncementTemplate? Template { get; set; }
    public ICollection<AnnouncementTeam> TargetTeams { get; set; } = new List<AnnouncementTeam>();
    public ICollection<AnnouncementAcknowledgment> Acknowledgments { get; set; } = new List<AnnouncementAcknowledgment>();
}
