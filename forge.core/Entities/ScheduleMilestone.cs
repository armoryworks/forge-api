namespace Forge.Core.Entities;

public class ScheduleMilestone : BaseEntity
{
    public int SalesOrderLineId { get; set; }
    public int? JobId { get; set; }
    public string MilestoneType { get; set; } = string.Empty;
    public DateTimeOffset TargetDate { get; set; }
    public DateTimeOffset? ActualDate { get; set; }
    public string? Notes { get; set; }

    public SalesOrderLine SalesOrderLine { get; set; } = null!;
    public Job? Job { get; set; }
}
