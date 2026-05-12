using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Webhooks;

public record GetWebhookDeliveriesQuery(int SubscriptionId) : IRequest<List<WebhookDeliveryResponseModel>>;

public class GetWebhookDeliveriesHandler(AppDbContext db) : IRequestHandler<GetWebhookDeliveriesQuery, List<WebhookDeliveryResponseModel>>
{
    public async Task<List<WebhookDeliveryResponseModel>> Handle(GetWebhookDeliveriesQuery request, CancellationToken cancellationToken)
    {
        var exists = await db.WebhookSubscriptions.AnyAsync(s => s.Id == request.SubscriptionId, cancellationToken);
        if (!exists)
            throw new KeyNotFoundException($"Webhook subscription {request.SubscriptionId} not found.");

        var deliveries = await db.WebhookDeliveries
            .AsNoTracking()
            .Where(d => d.SubscriptionId == request.SubscriptionId)
            .OrderByDescending(d => d.AttemptedAt)
            .Take(200)
            .ToListAsync(cancellationToken);

        return deliveries.Select(d => new WebhookDeliveryResponseModel(
            d.Id,
            d.SubscriptionId,
            d.EventType,
            d.StatusCode,
            d.DurationMs,
            d.AttemptedAt,
            d.AttemptNumber,
            d.IsSuccess,
            d.ErrorMessage)).ToList();
    }
}
