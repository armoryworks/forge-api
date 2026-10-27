using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Webhooks;

public record DeleteWebhookSubscriptionCommand(int Id) : IRequest;

public class DeleteWebhookSubscriptionHandler(AppDbContext db, IClock clock) : IRequestHandler<DeleteWebhookSubscriptionCommand>
{
    public async Task Handle(DeleteWebhookSubscriptionCommand request, CancellationToken cancellationToken)
    {
        var subscription = await db.WebhookSubscriptions.FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Webhook subscription {request.Id} not found.");

        subscription.IsActive = false;
        subscription.DeletedAt = clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }
}
