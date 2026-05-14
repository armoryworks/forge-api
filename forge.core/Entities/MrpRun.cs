
namespace Forge.Core.Entities;

public class MrpRun : BaseAuditableEntity
{
    public string RunNumber { get; set; } = string.Empty;
    public MrpRunType RunType { get; set; } = MrpRunType.Full;
    public MrpRunStatus Status { get; set; } = MrpRunStatus.Queued;
    public bool IsSimulation { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int PlanningHorizonDays { get; set; } = 90;
    public int TotalDemandCount { get; set; }
    public int TotalSupplyCount { get; set; }
    public int PlannedOrderCount { get; set; }
    public int ExceptionCount { get; set; }
    public string? ErrorMessage { get; set; }
    public int? InitiatedByUserId { get; set; }

    public ICollection<MrpDemand> Demands { get; set; } = [];
    public ICollection<MrpSupply> Supplies { get; set; } = [];
    public ICollection<MrpPlannedOrder> PlannedOrders { get; set; } = [];
    public ICollection<MrpException> Exceptions { get; set; } = [];
}
