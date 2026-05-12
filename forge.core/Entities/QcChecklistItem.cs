namespace Forge.Core.Entities;

public class QcChecklistItem : BaseEntity
{
    public int TemplateId { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? Specification { get; set; }
    public int SortOrder { get; set; }
    public bool IsRequired { get; set; } = true;

    public QcChecklistTemplate Template { get; set; } = null!;
}
