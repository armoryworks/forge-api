using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class ConsignmentAgreement : BaseAuditableEntity
{
    public ConsignmentDirection Direction { get; set; }
    public int? VendorId { get; set; }
    public int? CustomerId { get; set; }
    public int PartId { get; set; }
    public decimal AgreedUnitPrice { get; set; }
    public decimal? MinStockQuantity { get; set; }
    public decimal? MaxStockQuantity { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool InvoiceOnConsumption { get; set; } = true;
    public ConsignmentAgreementStatus Status { get; set; } = ConsignmentAgreementStatus.Active;
    public string? Terms { get; set; }
    public int ReconciliationFrequencyDays { get; set; } = 30;

    public Vendor? Vendor { get; set; }
    public Customer? Customer { get; set; }
    public Part Part { get; set; } = null!;
    public ICollection<ConsignmentTransaction> Transactions { get; set; } = [];
}
