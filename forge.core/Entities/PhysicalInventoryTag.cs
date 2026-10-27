using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class PhysicalInventoryTag : BaseEntity
{
    public int PhysicalInventoryId { get; set; }
    public int TagNumber { get; set; }
    public int LocationId { get; set; }
    public int? BinContentId { get; set; }
    public int PartId { get; set; }
    public string? LotNumber { get; set; }
    public string? SerialNumber { get; set; }
    public decimal FrozenQuantity { get; set; }
    public decimal? CountedQuantity { get; set; }
    public decimal? RecountQuantity { get; set; }
    public int? CountedByUserId { get; set; }
    public DateTimeOffset? CountedAt { get; set; }
    public int? RecountedByUserId { get; set; }
    public DateTimeOffset? RecountedAt { get; set; }
    public PhysicalInventoryTagStatus Status { get; set; } = PhysicalInventoryTagStatus.Open;
    public decimal? UnitCost { get; set; }
    public decimal? VarianceValue { get; set; }
    public int? BinMovementId { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public decimal? FinalQuantity => RecountQuantity ?? CountedQuantity;

    public PhysicalInventory PhysicalInventory { get; set; } = null!;
    public StorageLocation Location { get; set; } = null!;
    public BinContent? BinContent { get; set; }
    public Part Part { get; set; } = null!;
}
