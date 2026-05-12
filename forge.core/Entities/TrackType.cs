namespace Forge.Core.Entities;

public class TrackType : BaseAuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsDefault { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsShopFloor { get; set; } = true;

    // Custom field definitions (JSONB)
    public string? CustomFieldDefinitions { get; set; }

    public ICollection<JobStage> Stages { get; set; } = [];
    public ICollection<Job> Jobs { get; set; } = [];
}
