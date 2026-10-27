using Forge.Core.Entities;

namespace Forge.Api.Features.Notifications;

public record PlannedNotificationEmail(
    Notification Notification,
    NotificationEmailRecipient Recipient,
    NotificationEmailClass Class,
    string CoalesceKey,
    string Language);
