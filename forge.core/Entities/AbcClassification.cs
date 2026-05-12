using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class AbcClassification : BaseEntity
{
    public int PartId { get; set; }
    public AbcClass Classification { get; set; }
    public decimal AnnualUsageValue { get; set; }
    public decimal AnnualDemandQuantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal CumulativePercent { get; set; }
    public int Rank { get; set; }
    public DateTimeOffset CalculatedAt { get; set; }
    public int RunId { get; set; }

    public Part Part { get; set; } = null!;
    public AbcClassificationRun Run { get; set; } = null!;
}
