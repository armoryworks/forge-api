namespace Forge.Core.Models;

public record CreateNotificationRequestModel(
    int UserId,
    string Type,
    string Severity,
    string Source,
    string Title,
    string Message,
    string? EntityType,
    int? EntityId,
    int? SenderId);
