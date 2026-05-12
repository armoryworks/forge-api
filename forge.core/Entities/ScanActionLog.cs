using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class ScanActionLog : BaseAuditableEntity
{
    public int UserId { get; set; }
    public ScanActionType ActionType { get; set; }
    public int? PartId { get; set; }
    public string? PartNumber { get; set; }
    public int? FromLocationId { get; set; }
    public int? ToLocationId { get; set; }
    public decimal Quantity { get; set; }
    public int? RelatedEntityId { get; set; }
    public string? RelatedEntityType { get; set; }
    public int? ReversedByLogId { get; set; }
    public int? ReversesLogId { get; set; }
    public bool IsReversed { get; set; }
    public bool IsTrainingMode { get; set; }
    public string? KioskId { get; set; }
    public string? DeviceId { get; set; }

    public Part? Part { get; set; }
    public StorageLocation? FromLocation { get; set; }
    public StorageLocation? ToLocation { get; set; }
}
