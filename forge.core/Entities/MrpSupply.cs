using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class MrpSupply : BaseEntity
{
    public int MrpRunId { get; set; }
    public int PartId { get; set; }
    public MrpSupplySource Source { get; set; }
    public int? SourceEntityId { get; set; }
    public decimal Quantity { get; set; }
    public DateTimeOffset AvailableDate { get; set; }
    public decimal AllocatedQuantity { get; set; }

    public MrpRun MrpRun { get; set; } = null!;
    public Part Part { get; set; } = null!;
}
