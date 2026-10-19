using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Webhooks;

public class WebhookEventPublisher(
    AppDbContext db,
    IIntegrationOutboxService outbox,
    ICapabilitySnapshotProvider capabilities,
    IClock clock) : IWebhookEventPublisher
{
    internal const string CapabilityCode = "CAP-CROSS-WEBHOOKS";
    private static readonly JsonSerializerOptions BodyJsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<int> PublishAsync(
        string eventType,
        string entityType,
        int entityId,
        object data,
        string? occurrenceKey = null,
        CancellationToken ct = default)
    {
        if (!capabilities.IsEnabled(CapabilityCode)) return 0;

        var subscriptions = await db.WebhookSubscriptions
            .AsNoTracking()
            .Where(s => s.IsActive)
            .ToListAsync(ct);

        var matching = subscriptions
            .Where(s => WebhookEventTypes.Subscribes(s.EventTypesJson, eventType))
            .ToList();

        foreach (var subscription in matching)
        {
            var deliveryId = Guid.NewGuid();
            var body = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["id"] = deliveryId,
                ["event"] = eventType,
                ["occurredAt"] = clock.UtcNow,
                ["entityType"] = entityType,
                ["entityId"] = entityId,
                ["data"] = data,
            }, BodyJsonOptions);

            var payload = JsonSerializer.Serialize(
                new WebhookOutboxPayload(subscription.Id, eventType, deliveryId, body));

            var operationKey = occurrenceKey is null
                ? $"webhook:{subscription.Id}:{eventType}:{entityId}"
                : $"webhook:{subscription.Id}:{eventType}:{entityId}:{occurrenceKey}";

            await outbox.EnqueueAsync(
                IntegrationProvider.Webhook,
                operationKey,
                payload,
                entityType,
                entityId,
                maxAttempts: Math.Max(1, subscription.MaxRetries + 1),
                ct);
        }

        return matching.Count;
    }
}
