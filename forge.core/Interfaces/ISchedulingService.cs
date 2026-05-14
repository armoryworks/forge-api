using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface ISchedulingService
{
    Task<ScheduleRunResponseModel> ScheduleAsync(ScheduleParameters parameters, CancellationToken ct);
    Task<ScheduleRunResponseModel> SimulateAsync(ScheduleParameters parameters, CancellationToken ct);
    Task RescheduleOperationAsync(int scheduledOperationId, DateTimeOffset newStart, CancellationToken ct);
    Task<WorkCenterLoadResponseModel> GetWorkCenterLoadAsync(int workCenterId, DateOnly from, DateOnly to, CancellationToken ct);
    Task<IReadOnlyList<DispatchListItemModel>> GetDispatchListAsync(int workCenterId, CancellationToken ct);
    decimal CalculateAvailableCapacity(int workCenterId, DateOnly date, IReadOnlyList<WorkCenterShiftInfo> shifts, IReadOnlyDictionary<DateOnly, decimal> calendarOverrides);
}

public record ScheduleParameters(
    ScheduleDirection Direction,
    DateOnly ScheduleFrom,
    DateOnly ScheduleTo,
    int[]? JobIdFilter,
    string PriorityRule,
    int? RunByUserId);

public record WorkCenterShiftInfo(
    decimal NetHours,
    DaysOfWeek DaysOfWeek);
