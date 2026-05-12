using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class JobActivityLog : BaseEntity
{
    public int JobId { get; set; }
    public int? UserId { get; set; }
    public ActivityAction Action { get; set; }
    public string? FieldName { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Frozen at write time: where the work was actually happening when this
    // event occurred. The job's routing may be edited later (work center
    // swapped, operation renumbered) — this row preserves the truth as it
    // was, so audit/reporting reflects the actual shop-floor state.
    public int? WorkCenterId { get; set; }
    public int? OperationId { get; set; }

    public Job Job { get; set; } = null!;
    public WorkCenter? WorkCenter { get; set; }
    public Operation? Operation { get; set; }
}
