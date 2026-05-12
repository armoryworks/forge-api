namespace Forge.Core.Models;

public record WebhookSubscriptionResponseModel(
    int Id,
    string Url,
    string EventTypesJson,
    bool IsActive,
    int FailureCount,
    int MaxRetries,
    DateTimeOffset? LastDeliveredAt,
    DateTimeOffset? LastFailedAt,
    bool AutoDisableOnFailure,
    string? Description,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
