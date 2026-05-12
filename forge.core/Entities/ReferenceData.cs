namespace Forge.Core.Entities;

public class ReferenceData : BaseEntity
{
    public string GroupCode { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsSeedData { get; set; }
    public DateTimeOffset? EffectiveFrom { get; set; }
    public DateTimeOffset? EffectiveTo { get; set; }
    public int? ParentId { get; set; }
    public string? Metadata { get; set; }

    public ReferenceData? Parent { get; set; }
    public ICollection<ReferenceData> Children { get; set; } = [];
}
