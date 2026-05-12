using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class MrpDemand : BaseEntity
{
    public int MrpRunId { get; set; }
    public int PartId { get; set; }
    public MrpDemandSource Source { get; set; }
    public int? SourceEntityId { get; set; }
    public decimal Quantity { get; set; }
    public DateTimeOffset RequiredDate { get; set; }
    public bool IsDependent { get; set; }
    public int? ParentPlannedOrderId { get; set; }
    public int BomLevel { get; set; }

    public MrpRun MrpRun { get; set; } = null!;
    public Part Part { get; set; } = null!;
    public MrpPlannedOrder? ParentPlannedOrder { get; set; }
}
