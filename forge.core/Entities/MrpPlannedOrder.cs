using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class MrpPlannedOrder : BaseAuditableEntity
{
    public int MrpRunId { get; set; }
    public int PartId { get; set; }
    public MrpOrderType OrderType { get; set; }
    public MrpPlannedOrderStatus Status { get; set; } = MrpPlannedOrderStatus.Planned;
    public decimal Quantity { get; set; }
    public DateTimeOffset StartDate { get; set; }
    public DateTimeOffset DueDate { get; set; }
    public bool IsFirmed { get; set; }
    public int? ReleasedPurchaseOrderId { get; set; }
    public int? ReleasedJobId { get; set; }
    public int? ParentPlannedOrderId { get; set; }
    public string? Notes { get; set; }

    public MrpRun MrpRun { get; set; } = null!;
    public Part Part { get; set; } = null!;
    public PurchaseOrder? ReleasedPurchaseOrder { get; set; }
    public Job? ReleasedJob { get; set; }
    public MrpPlannedOrder? ParentPlannedOrder { get; set; }
    public ICollection<MrpPlannedOrder> ChildPlannedOrders { get; set; } = [];
    public ICollection<MrpDemand> DependentDemands { get; set; } = [];
}
