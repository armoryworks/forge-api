namespace Forge.Core.Models;

public record WorkCenterLoadResponseModel(
    int WorkCenterId,
    string WorkCenterName,
    IReadOnlyList<WorkCenterLoadBucket> Buckets);

public record WorkCenterLoadBucket(
    DateOnly WeekStart,
    decimal CapacityHours,
    decimal ScheduledHours,
    decimal UtilizationPercent);
