using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Webhooks;

public class WebhookManualDelivery(AppDbContext db, IWebhookDeliveryService delivery)
{
    public async Task<WebhookDeliveryResponseModel> SendAsync(
        WebhookSubscription subscription, string eventType, string body, CancellationToken ct)
    {
        if (!subscription.IsActive)
            throw new InvalidOperationException("The webhook subscription is inactive. Activate it before sending to it.");

        var lastId = await db.WebhookDeliveries
            .Where(d => d.SubscriptionId == subscription.Id)
            .MaxAsync(d => (int?)d.Id, ct) ?? 0;

        var entry = new IntegrationOutboxEntry
        {
            Provider = IntegrationProvider.Webhook,
            OperationKey = $"webhook-manual:{subscription.Id}:{Guid.NewGuid():N}",
            Payload = JsonSerializer.Serialize(new WebhookOutboxPayload(subscription.Id, eventType, Guid.NewGuid(), body)),
            AttemptCount = 1,
            MaxAttempts = 1,
        };

        string? failure = null;
        try
        {
            await delivery.DeliverAsync(entry, ct);
        }
        catch (HttpRequestException ex)
        {
            failure = ex.Message;
        }

        var row = await db.WebhookDeliveries
            .AsNoTracking()
            .Where(d => d.SubscriptionId == subscription.Id && d.Id > lastId)
            .OrderByDescending(d => d.Id)
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException(failure ?? "The webhook was not sent.");

        return WebhookSubscriptionMapping.ToModel(row);
    }
}
