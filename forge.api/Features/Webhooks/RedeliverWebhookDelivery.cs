using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Webhooks;

public record RedeliverWebhookDeliveryCommand(int DeliveryId) : IRequest<WebhookDeliveryResponseModel>;

public class RedeliverWebhookDeliveryHandler(
    AppDbContext db,
    WebhookManualDelivery manualDelivery) : IRequestHandler<RedeliverWebhookDeliveryCommand, WebhookDeliveryResponseModel>
{
    public async Task<WebhookDeliveryResponseModel> Handle(RedeliverWebhookDeliveryCommand request, CancellationToken cancellationToken)
    {
        var original = await db.WebhookDeliveries.AsNoTracking().FirstOrDefaultAsync(d => d.Id == request.DeliveryId, cancellationToken)
            ?? throw new KeyNotFoundException($"Webhook delivery {request.DeliveryId} not found.");

        var subscription = await db.WebhookSubscriptions.FirstOrDefaultAsync(s => s.Id == original.SubscriptionId, cancellationToken)
            ?? throw new KeyNotFoundException($"Webhook subscription {original.SubscriptionId} not found.");

        return await manualDelivery.SendAsync(subscription, original.EventType, original.PayloadJson, cancellationToken);
    }
}
