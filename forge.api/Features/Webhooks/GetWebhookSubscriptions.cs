using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Webhooks;

public record GetWebhookSubscriptionsQuery : IRequest<List<WebhookSubscriptionResponseModel>>;

public class GetWebhookSubscriptionsHandler(AppDbContext db) : IRequestHandler<GetWebhookSubscriptionsQuery, List<WebhookSubscriptionResponseModel>>
{
    public async Task<List<WebhookSubscriptionResponseModel>> Handle(GetWebhookSubscriptionsQuery request, CancellationToken cancellationToken)
    {
        var subscriptions = await db.WebhookSubscriptions
            .AsNoTracking()
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(cancellationToken);

        return subscriptions.Select(s => WebhookSubscriptionMapping.ToModel(s)).ToList();
    }
}
