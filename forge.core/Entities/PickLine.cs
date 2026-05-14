
namespace Forge.Core.Entities;

public class PickLine : BaseEntity
{
    public int WaveId { get; set; }
    public int ShipmentLineId { get; set; }
    public int PartId { get; set; }
    public int FromLocationId { get; set; }
    public int? FromBinId { get; set; }
    public string? BinPath { get; set; }
    public decimal RequestedQuantity { get; set; }
    public decimal PickedQuantity { get; set; }
    public PickLineStatus Status { get; set; } = PickLineStatus.Pending;
    public int SortOrder { get; set; }
    public int? PickedByUserId { get; set; }
    public DateTimeOffset? PickedAt { get; set; }
    public string? ShortNotes { get; set; }

    public PickWave Wave { get; set; } = null!;
    public ShipmentLine ShipmentLine { get; set; } = null!;
    public Part Part { get; set; } = null!;
    public StorageLocation FromLocation { get; set; } = null!;
}
