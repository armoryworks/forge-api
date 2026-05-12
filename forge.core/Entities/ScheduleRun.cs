using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class ScheduleRun : BaseAuditableEntity
{
    public DateTimeOffset RunDate { get; set; }
    public ScheduleDirection Direction { get; set; }
    public ScheduleRunStatus Status { get; set; }
    public string ParametersJson { get; set; } = "{}";
    public int OperationsScheduled { get; set; }
    public int ConflictsDetected { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int RunByUserId { get; set; }
    public string? ErrorMessage { get; set; }

    public ICollection<ScheduledOperation> Operations { get; set; } = [];
}
