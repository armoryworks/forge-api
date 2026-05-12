namespace Forge.Core.Entities;

public class WebhookDelivery : BaseEntity
{
    public int SubscriptionId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public int? StatusCode { get; set; }
    public string? ResponseBody { get; set; }
    public decimal DurationMs { get; set; }
    public DateTimeOffset AttemptedAt { get; set; }
    public int AttemptNumber { get; set; } = 1;
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }

    public WebhookSubscription Subscription { get; set; } = null!;
}
