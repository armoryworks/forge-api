using Forge.Core.Entities;

namespace Forge.Api.Features.Webhooks;

public interface IWebhookDeliveryService
{
    Task DeliverAsync(IntegrationOutboxEntry entry, CancellationToken ct);
}
