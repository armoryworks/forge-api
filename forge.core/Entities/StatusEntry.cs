namespace Forge.Core.Entities;

public class StatusEntry : BaseAuditableEntity
{
    public string EntityType { get; set; } = string.Empty;
    public int EntityId { get; set; }
    public string StatusCode { get; set; } = string.Empty;
    public string StatusLabel { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public string? Notes { get; set; }
    public int? SetById { get; set; }

    // Frozen at write time: where the entity was when this status change
    // happened. Only meaningful when EntityType="Job" — null on other
    // status entries (Customer hold, PO status, etc.). Preserves the truth
    // even if the job's routing is later edited.
    public int? WorkCenterId { get; set; }
    public int? OperationId { get; set; }

    public WorkCenter? WorkCenter { get; set; }
    public Operation? Operation { get; set; }
}
