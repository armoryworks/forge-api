using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class PhysicalInventory : BaseAuditableEntity, IConcurrencyVersioned
{
    public string InventoryNumber { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? ScopeLocationId { get; set; }
    public PhysicalInventoryStatus Status { get; set; } = PhysicalInventoryStatus.Draft;
    public DateOnly CountDate { get; set; }
    public bool BlindCount { get; set; } = true;
    public decimal RecountTolerancePercent { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset? FrozenAt { get; set; }
    public int? FrozenByUserId { get; set; }
    public DateTimeOffset? PostedAt { get; set; }
    public int? PostedByUserId { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public int? CancelledByUserId { get; set; }
    public decimal TotalVarianceValue { get; set; }
    public int? CreatedByUserId { get; set; }
    public uint Version { get; set; }

    public StorageLocation? ScopeLocation { get; set; }
    public ICollection<PhysicalInventoryTag> Tags { get; set; } = [];
}
