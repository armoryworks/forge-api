
namespace Forge.Core.Entities;

public class ScheduledOperation : BaseAuditableEntity
{
    public int JobId { get; set; }
    public int OperationId { get; set; }
    public int WorkCenterId { get; set; }
    public DateTimeOffset ScheduledStart { get; set; }
    public DateTimeOffset ScheduledEnd { get; set; }
    public DateTimeOffset? SetupStart { get; set; }
    public DateTimeOffset? SetupEnd { get; set; }
    public DateTimeOffset? RunStart { get; set; }
    public DateTimeOffset? RunEnd { get; set; }
    public decimal SetupHours { get; set; }
    public decimal RunHours { get; set; }
    public decimal TotalHours { get; set; }
    public ScheduledOperationStatus Status { get; set; }
    public int SequenceNumber { get; set; }
    public bool IsLocked { get; set; }
    public int? ScheduleRunId { get; set; }

    public Job Job { get; set; } = null!;
    public Operation Operation { get; set; } = null!;
    public WorkCenter WorkCenter { get; set; } = null!;
    public ScheduleRun? ScheduleRun { get; set; }
}
