using Forge.Core.Enums;

namespace Forge.Core.Models;

public record ConsignmentAgreementResponseModel
{
    public int Id { get; init; }
    public ConsignmentDirection Direction { get; init; }
    public int? VendorId { get; init; }
    public string? VendorName { get; init; }
    public int? CustomerId { get; init; }
    public string? CustomerName { get; init; }
    public int PartId { get; init; }
    public string PartNumber { get; init; } = string.Empty;
    public string PartDescription { get; init; } = string.Empty;
    public decimal AgreedUnitPrice { get; init; }
    public decimal? MinStockQuantity { get; init; }
    public decimal? MaxStockQuantity { get; init; }
    public DateOnly EffectiveFrom { get; init; }
    public DateOnly? EffectiveTo { get; init; }
    public bool InvoiceOnConsumption { get; init; }
    public ConsignmentAgreementStatus Status { get; init; }
    public string? Terms { get; init; }
    public int ReconciliationFrequencyDays { get; init; }
    public int TransactionCount { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
