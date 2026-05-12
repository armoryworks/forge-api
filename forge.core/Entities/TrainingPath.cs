namespace Forge.Core.Entities;

public class TrainingPath : BaseAuditableEntity
{
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Icon { get; set; } = "school";
    public string? AllowedRoles { get; set; }
    public bool IsAutoAssigned { get; set; } = false;
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    public ICollection<TrainingPathModule> PathModules { get; set; } = [];
    public ICollection<TrainingPathEnrollment> Enrollments { get; set; } = [];
}
