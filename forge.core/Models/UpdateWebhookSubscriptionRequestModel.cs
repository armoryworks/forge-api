namespace Forge.Core.Models;

public record UpdateWebhookSubscriptionRequestModel(
    string Url,
    string EventTypesJson,
    string? Description,
    string? HeadersJson,
    int MaxRetries,
    bool AutoDisableOnFailure,
    bool IsActive,
    bool RotateSecret = false);
