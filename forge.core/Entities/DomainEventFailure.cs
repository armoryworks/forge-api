
namespace Forge.Core.Entities;

public class DomainEventFailure : BaseEntity
{
    public string EventType { get; set; } = string.Empty;
    public string EventPayload { get; set; } = string.Empty;
    public string HandlerName { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public DateTimeOffset FailedAt { get; set; }
    public int RetryCount { get; set; }
    public DateTimeOffset? LastRetryAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public DomainEventFailureStatus Status { get; set; } = DomainEventFailureStatus.Failed;
}
