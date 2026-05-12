namespace Forge.Core.Models;

public record WebhookDeliveryResponseModel(
    int Id,
    int SubscriptionId,
    string EventType,
    int? StatusCode,
    decimal DurationMs,
    DateTimeOffset AttemptedAt,
    int AttemptNumber,
    bool IsSuccess,
    string? ErrorMessage);
