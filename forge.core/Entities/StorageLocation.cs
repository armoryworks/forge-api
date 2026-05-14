
namespace Forge.Core.Entities;

public class StorageLocation : BaseAuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public LocationType LocationType { get; set; }
    public int? ParentId { get; set; }
    public string? Barcode { get; set; }
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public StorageLocation? Parent { get; set; }
    public ICollection<StorageLocation> Children { get; set; } = [];
    public ICollection<BinContent> Contents { get; set; } = [];
}
