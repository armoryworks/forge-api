
namespace Forge.Core.Entities;

public class SyncQueueEntry : BaseEntity
{
    public string EntityType { get; set; } = string.Empty;
    public int EntityId { get; set; }
    public string Operation { get; set; } = string.Empty;
    public string? Payload { get; set; }
    public SyncStatus Status { get; set; } = SyncStatus.Pending;
    public int AttemptCount { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ProcessedAt { get; set; }
}
