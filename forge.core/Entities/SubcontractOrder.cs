namespace Forge.Core.Entities;

using Forge.Core.Enums;

public class SubcontractOrder : BaseAuditableEntity
{
    public int JobId { get; set; }
    public int OperationId { get; set; }
    public int VendorId { get; set; }
    public int? PurchaseOrderId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public DateTimeOffset SentAt { get; set; }
    public DateTimeOffset? ExpectedReturnDate { get; set; }
    public DateTimeOffset? ReceivedAt { get; set; }
    public int? ReceivedById { get; set; }
    public decimal? ReceivedQuantity { get; set; }
    public SubcontractStatus Status { get; set; } = SubcontractStatus.Pending;
    public string? ShippingTrackingNumber { get; set; }
    public string? ReturnTrackingNumber { get; set; }
    public string? Notes { get; set; }
    public int? NcrId { get; set; }

    public Job Job { get; set; } = null!;
    public Operation Operation { get; set; } = null!;
    public Vendor Vendor { get; set; } = null!;
    public PurchaseOrder? PurchaseOrder { get; set; }
}
