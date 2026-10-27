using Forge.Core.Enums;

namespace Forge.Core.Models;

public record ConsignmentAgreementStockResponseModel
{
    public int AgreementId { get; init; }
    public ConsignmentDirection Direction { get; init; }
    public ConsignmentAgreementStatus Status { get; init; }
    public string OwnerName { get; init; } = string.Empty;
    public int PartId { get; init; }
    public string PartNumber { get; init; } = string.Empty;
    public decimal AgreedUnitPrice { get; init; }
    public decimal OnHandQuantity { get; init; }
    public decimal OnHandValue { get; init; }
    public decimal UnbilledQuantity { get; init; }
    public decimal UnbilledValue { get; init; }
    public decimal? MinStockQuantity { get; init; }
    public decimal? MaxStockQuantity { get; init; }
    public bool BelowMinimum { get; init; }
    public bool AboveMaximum { get; init; }
}
