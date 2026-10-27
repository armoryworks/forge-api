namespace Forge.Core.Models;

public record ConsignmentReconciliationResponseModel
{
    public int AgreementId { get; init; }
    public decimal BookQuantity { get; init; }
    public decimal PhysicalQuantity { get; init; }
    public decimal Variance { get; init; }
    public int? AdjustmentTransactionId { get; init; }
    public DateOnly? PeriodFrom { get; init; }
    public DateOnly? PeriodTo { get; init; }
    public IReadOnlyList<ConsignmentTransactionResponseModel> ConsumedTransactions { get; init; } = [];
    public decimal ConsumedQuantity { get; init; }
    public decimal ConsumedAmount { get; init; }
    public int? VendorBillId { get; init; }
    public string? VendorBillNumber { get; init; }
    public bool BillingAvailable { get; init; }
}
