using Forge.Core.Enums;

namespace Forge.Core.Models;

public record UpdateConsignmentAgreementRequestModel
{
    public decimal? AgreedUnitPrice { get; init; }
    public decimal? MinStockQuantity { get; init; }
    public decimal? MaxStockQuantity { get; init; }
    public DateOnly? EffectiveTo { get; init; }
    public bool? InvoiceOnConsumption { get; init; }
    public ConsignmentAgreementStatus? Status { get; init; }
    public string? Terms { get; init; }
    public int? ReconciliationFrequencyDays { get; init; }
}
