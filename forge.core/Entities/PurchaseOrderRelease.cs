
namespace Forge.Core.Entities;

public class PurchaseOrderRelease : BaseAuditableEntity
{
    public int PurchaseOrderId { get; set; }
    public int ReleaseNumber { get; set; }
    public int PurchaseOrderLineId { get; set; }
    public decimal Quantity { get; set; }
    public DateTimeOffset RequestedDeliveryDate { get; set; }
    public DateTimeOffset? ActualDeliveryDate { get; set; }
    public PurchaseOrderReleaseStatus Status { get; set; } = PurchaseOrderReleaseStatus.Open;
    public int? ReceivingRecordId { get; set; }
    public string? Notes { get; set; }

    public PurchaseOrder PurchaseOrder { get; set; } = null!;
    public PurchaseOrderLine PurchaseOrderLine { get; set; } = null!;
    public ReceivingRecord? ReceivingRecord { get; set; }
}
