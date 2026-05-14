
namespace Forge.Core.Models;

public record ScheduleRunResponseModel(
    int Id,
    DateTimeOffset RunDate,
    ScheduleDirection Direction,
    ScheduleRunStatus Status,
    int OperationsScheduled,
    int ConflictsDetected,
    DateTimeOffset? CompletedAt,
    int RunByUserId,
    string? ErrorMessage);
