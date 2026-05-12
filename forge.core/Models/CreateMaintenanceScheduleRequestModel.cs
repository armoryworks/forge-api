namespace Forge.Core.Models;

public record CreateMaintenanceScheduleRequestModel(
    int AssetId,
    string Title,
    string? Description,
    int IntervalDays,
    decimal? IntervalHours,
    DateTimeOffset NextDueAt);
