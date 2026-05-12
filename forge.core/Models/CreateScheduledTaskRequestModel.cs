namespace Forge.Core.Models;

public record CreateScheduledTaskRequestModel(
    string Name,
    string? Description,
    int TrackTypeId,
    int? InternalProjectTypeId,
    int? AssigneeId,
    string CronExpression);
