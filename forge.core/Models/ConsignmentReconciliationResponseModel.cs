namespace Forge.Core.Models;

public record ConsignmentReconciliationResponseModel
{
    public int AgreementId { get; init; }
    public decimal BookQuantity { get; init; }
    public decimal PhysicalQuantity { get; init; }
    public decimal Variance { get; init; }
    public int? AdjustmentTransactionId { get; init; }
}
