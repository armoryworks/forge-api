namespace Forge.Core.Models;

public record PhysicalInventoryVarianceLineResponseModel
{
    public int TagId { get; init; }
    public int TagNumber { get; init; }
    public int PartId { get; init; }
    public string PartNumber { get; init; } = string.Empty;
    public string? PartDescription { get; init; }
    public string LocationName { get; init; } = string.Empty;
    public string? LotNumber { get; init; }
    public decimal FrozenQuantity { get; init; }
    public decimal CountedQuantity { get; init; }
    public decimal VarianceQuantity { get; init; }
    public decimal? VariancePercent { get; init; }
    public decimal UnitCost { get; init; }
    public decimal VarianceValue { get; init; }
    public bool Recounted { get; init; }
}
