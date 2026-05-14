
namespace Forge.Core.Models;

public record CreateConsignmentAgreementRequestModel
{
    public ConsignmentDirection Direction { get; init; }
    public int? VendorId { get; init; }
    public int? CustomerId { get; init; }
    public int PartId { get; init; }
    public decimal AgreedUnitPrice { get; init; }
    public decimal? MinStockQuantity { get; init; }
    public decimal? MaxStockQuantity { get; init; }
    public DateOnly EffectiveFrom { get; init; }
    public DateOnly? EffectiveTo { get; init; }
    public bool InvoiceOnConsumption { get; init; } = true;
    public string? Terms { get; init; }
    public int ReconciliationFrequencyDays { get; init; } = 30;
}
