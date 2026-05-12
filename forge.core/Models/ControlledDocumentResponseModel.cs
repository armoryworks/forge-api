using Forge.Core.Enums;

namespace Forge.Core.Models;

public record ControlledDocumentResponseModel(
    int Id,
    string DocumentNumber,
    string Title,
    string? Description,
    string Category,
    int CurrentRevision,
    ControlledDocumentStatus Status,
    int OwnerId,
    int? CheckedOutById,
    DateTimeOffset? CheckedOutAt,
    DateTimeOffset? ReleasedAt,
    DateTimeOffset? ReviewDueDate,
    int ReviewIntervalDays,
    int RevisionCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
