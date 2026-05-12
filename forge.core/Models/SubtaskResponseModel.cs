namespace Forge.Core.Models;

public record SubtaskResponseModel(
    int Id,
    int JobId,
    string Text,
    bool IsCompleted,
    int? AssigneeId,
    int SortOrder,
    DateTimeOffset? CompletedAt);
