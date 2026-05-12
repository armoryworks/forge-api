namespace Forge.Core.Models;

public record ScheduledTaskResponseModel(
    int Id,
    string Name,
    string? Description,
    int TrackTypeId,
    string TrackTypeName,
    int? InternalProjectTypeId,
    int? AssigneeId,
    string CronExpression,
    bool IsActive,
    DateTimeOffset? LastRunAt,
    DateTimeOffset? NextRunAt,
    DateTimeOffset CreatedAt);
