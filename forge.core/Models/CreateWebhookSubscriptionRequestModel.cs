namespace Forge.Core.Models;

public record CreateWebhookSubscriptionRequestModel(
    string Url,
    string EventTypesJson,
    string Secret,
    string? Description,
    string? HeadersJson,
    int MaxRetries,
    bool AutoDisableOnFailure);
