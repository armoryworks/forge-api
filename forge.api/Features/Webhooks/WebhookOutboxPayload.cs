namespace Forge.Api.Features.Webhooks;

public record WebhookOutboxPayload(int SubscriptionId, string EventType, Guid DeliveryId, string Body);
