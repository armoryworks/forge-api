
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
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public ConsignmentAgreement Agreement { get; set; } = null!;
    public PurchaseOrder? PurchaseOrder { get; set; }
    public Invoice? Invoice { get; set; }
}
