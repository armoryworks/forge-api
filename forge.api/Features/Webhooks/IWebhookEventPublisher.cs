namespace Forge.Api.Features.Webhooks;

public interface IWebhookEventPublisher
{
    Task<int> PublishAsync(
        string eventType,
        string entityType,
        int entityId,
        object data,
        string? occurrenceKey = null,
        CancellationToken ct = default);
}
