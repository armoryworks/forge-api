using Forge.Core.Enums;

namespace Forge.Core.Models;

public record AbcClassificationResultResponseModel
{
    public int PartId { get; init; }
    public string PartNumber { get; init; } = string.Empty;
    public string PartDescription { get; init; } = string.Empty;
    public AbcClass Classification { get; init; }
    public decimal AnnualUsageValue { get; init; }
    public decimal AnnualDemandQuantity { get; init; }
    public decimal UnitCost { get; init; }
    public decimal CumulativePercent { get; init; }
    public int Rank { get; init; }
}
