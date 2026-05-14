
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
    public int? ReversedMovementId { get; set; }
    public int? ScanActionLogId { get; set; }

    public StorageLocation? FromLocation { get; set; }
    public StorageLocation? ToLocation { get; set; }
    public BinMovement? ReversedMovement { get; set; }
    public ScanActionLog? ScanActionLog { get; set; }
}
