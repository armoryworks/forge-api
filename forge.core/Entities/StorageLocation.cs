using Forge.Core.Enums;

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

    // Single-location mode: the one location used when the Locations sub-feature is
    // off, so stock can be tracked without the customer choosing a bin. At most one
    // active default is enforced by a filtered unique index (ix_storage_locations_is_default).
    public bool IsDefault { get; set; }

    public StorageLocation? Parent { get; set; }
    public ICollection<StorageLocation> Children { get; set; } = [];
    public ICollection<BinContent> Contents { get; set; } = [];
}
