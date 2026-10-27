using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class ConsignmentTransaction : BaseEntity
{
    public int AgreementId { get; set; }
    public ConsignmentTransactionType Type { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal ExtendedAmount { get; set; }
    public int? PurchaseOrderId { get; set; }
    public int? InvoiceId { get; set; }
    public int? BinContentId { get; set; }
    public int? BinMovementId { get; set; }
    public int? StorageLocationId { get; set; }
    public int? JobId { get; set; }
    public int? VendorBillId { get; set; }
    public long? JournalEntryId { get; set; }
    public int? CreatedByUserId { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public ConsignmentAgreement Agreement { get; set; } = null!;
    public PurchaseOrder? PurchaseOrder { get; set; }
    public Invoice? Invoice { get; set; }
    public VendorBill? VendorBill { get; set; }
}
