namespace Forge.Core.Entities;

public class PartRevision : BaseAuditableEntity
{
    public int PartId { get; set; }
    public string Revision { get; set; } = string.Empty;
    public string? ChangeDescription { get; set; }
    public string? ChangeReason { get; set; }
    public DateTimeOffset EffectiveDate { get; set; }
    public bool IsCurrent { get; set; }

    public Part Part { get; set; } = null!;
    public ICollection<FileAttachment> Files { get; set; } = new List<FileAttachment>();
}
