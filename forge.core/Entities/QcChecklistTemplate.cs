namespace Forge.Core.Entities;

public class QcChecklistTemplate : BaseAuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? PartId { get; set; }
    public bool IsActive { get; set; } = true;

    public Part? Part { get; set; }
    public ICollection<QcChecklistItem> Items { get; set; } = new List<QcChecklistItem>();
}
