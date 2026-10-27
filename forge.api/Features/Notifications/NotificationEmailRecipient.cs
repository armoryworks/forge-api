namespace Forge.Api.Features.Notifications;

public record NotificationEmailRecipient(int UserId, string? Email, string FirstName, bool IsActive);
