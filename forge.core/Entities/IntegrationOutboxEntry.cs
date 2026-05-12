using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class IntegrationOutboxEntry : BaseAuditableEntity
{
    public IntegrationProvider Provider { get; set; }
    public string OperationKey { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public OutboxStatus Status { get; set; } = OutboxStatus.Pending;
    public int AttemptCount { get; set; }
    public int MaxAttempts { get; set; } = 5;
    public DateTimeOffset? NextAttemptAt { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public string? LastError { get; set; }
    public string? EntityType { get; set; }
    public int? EntityId { get; set; }
}
