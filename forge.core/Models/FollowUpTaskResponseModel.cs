using Forge.Core.Enums;

namespace Forge.Core.Models;

public record FollowUpTaskResponseModel(
    int Id,
    string Title,
    string? Description,
    int AssignedToUserId,
    string AssignedToName,
    DateTimeOffset? DueDate,
    string? SourceEntityType,
    int? SourceEntityId,
    string? SourceEntityLabel,
    FollowUpTriggerType TriggerType,
    FollowUpStatus Status,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? DismissedAt,
    DateTimeOffset CreatedAt);
