using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class BinMovement : BaseEntity
{
    public string EntityType { get; set; } = "part";
    public int EntityId { get; set; }
    public decimal Quantity { get; set; }
    public string? LotNumber { get; set; }
    public int? FromLocationId { get; set; }
    public int? ToLocationId { get; set; }
    public int MovedBy { get; set; }
    public DateTimeOffset MovedAt { get; set; }
    public BinMovementReason? Reason { get; set; }
    /// <summary>
    /// Free-text reason / provenance for the movement (e.g. a manual inventory
    /// adjustment's justification, optionally a source PO / vendor reference).
    /// Operational audit only — never a GL posting (see inventory-override design).
    /// </summary>
    public string? Notes { get; set; }
    public int? ReversedMovementId { get; set; }
    public int? ScanActionLogId { get; set; }

    public StorageLocation? FromLocation { get; set; }
    public StorageLocation? ToLocation { get; set; }
    public BinMovement? ReversedMovement { get; set; }
    public ScanActionLog? ScanActionLog { get; set; }
}
