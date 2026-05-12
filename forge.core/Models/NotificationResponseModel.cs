namespace Forge.Core.Models;

public record NotificationResponseModel(
    int Id,
    string Type,
    string Severity,
    string Source,
    string Title,
    string Message,
    bool IsRead,
    bool IsPinned,
    bool IsDismissed,
    string? EntityType,
    int? EntityId,
    string? SenderInitials,
    string? SenderColor,
    DateTimeOffset CreatedAt);
