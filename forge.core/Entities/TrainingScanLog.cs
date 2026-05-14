
namespace Forge.Core.Entities;

public class TrainingScanLog : BaseEntity
{
    public int UserId { get; set; }
    public ScanActionType ActionType { get; set; }
    public int? PartId { get; set; }
    public int? FromLocationId { get; set; }
    public int? ToLocationId { get; set; }
    public int Quantity { get; set; }
    public int? JobId { get; set; }
    public int? PurchaseOrderId { get; set; }
    public int? ShipmentId { get; set; }
    public DateTimeOffset ScannedAt { get; set; }
    public bool WasSuccessful { get; set; }
    public string? ErrorMessage { get; set; }
}
