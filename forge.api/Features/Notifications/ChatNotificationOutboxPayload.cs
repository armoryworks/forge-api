namespace Forge.Api.Features.Notifications;

public record ChatNotificationOutboxPayload(
    int UserId,
    int IntegrationId,
    string ProviderId,
    string Title,
    string Body,
    string Severity,
    string? EntityType,
    int? EntityId);
