namespace Forge.Core.Entities;

public class WebhookSubscription : BaseAuditableEntity
{
    public string Url { get; set; } = string.Empty;
    public string EventTypesJson { get; set; } = "[]";
    public string EncryptedSecret { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int FailureCount { get; set; }
    public int MaxRetries { get; set; } = 5;
    public DateTimeOffset? LastDeliveredAt { get; set; }
    public DateTimeOffset? LastFailedAt { get; set; }
    public bool AutoDisableOnFailure { get; set; } = true;
    public string? Description { get; set; }
    public string? HeadersJson { get; set; }

    public ICollection<WebhookDelivery> Deliveries { get; set; } = [];
}
