namespace Forge.Core.Models;

public record ConsignmentStockSummaryResponseModel
{
    public int TotalAgreements { get; init; }
    public int ActiveAgreements { get; init; }
    public decimal TotalConsignedValue { get; init; }
    public IReadOnlyList<ConsignmentStockByOwnerResponseModel> ByOwner { get; init; } = [];
}

public record ConsignmentStockByOwnerResponseModel
{
    public string OwnerType { get; init; } = string.Empty;
    public int OwnerId { get; init; }
    public string OwnerName { get; init; } = string.Empty;
    public int AgreementCount { get; init; }
    public decimal TotalQuantity { get; init; }
    public decimal TotalValue { get; init; }
}
