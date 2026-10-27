namespace Forge.Core.Entities;

/// <summary>A job linked to a project's WBS element, so the project lists the jobs doing its work.</summary>
public class WbsElementJob : BaseEntity
{
    public int WbsElementId { get; set; }
    public int JobId { get; set; }
    public int? LinkedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public WbsElement WbsElement { get; set; } = null!;
    public Job Job { get; set; } = null!;
}
