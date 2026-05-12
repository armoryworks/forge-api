namespace Forge.Core.Models;

public record MaintenanceScheduleResponseModel(
    int Id,
    int AssetId,
    string AssetName,
    string Title,
    string? Description,
    int IntervalDays,
    decimal? IntervalHours,
    DateTimeOffset? LastPerformedAt,
    DateTimeOffset NextDueAt,
    bool IsActive,
    bool IsOverdue);
