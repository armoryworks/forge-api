using System.Text.Json;

using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Webhooks;

public record TestWebhookSubscriptionCommand(int Id) : IRequest<WebhookDeliveryResponseModel>;

public class TestWebhookSubscriptionHandler(
    AppDbContext db,
    WebhookManualDelivery manualDelivery,
    IClock clock) : IRequestHandler<TestWebhookSubscriptionCommand, WebhookDeliveryResponseModel>
{
    private static readonly JsonSerializerOptions BodyJsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<WebhookDeliveryResponseModel> Handle(TestWebhookSubscriptionCommand request, CancellationToken cancellationToken)
    {
        var subscription = await db.WebhookSubscriptions.FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Webhook subscription {request.Id} not found.");

        var body = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["id"] = Guid.NewGuid(),
            ["event"] = WebhookEventTypes.Ping,
            ["occurredAt"] = clock.UtcNow,
            ["entityType"] = "WebhookSubscription",
            ["entityId"] = subscription.Id,
            ["data"] = new { message = "Test delivery from Forge." },
        }, BodyJsonOptions);

        return await manualDelivery.SendAsync(subscription, WebhookEventTypes.Ping, body, cancellationToken);
    }
}
