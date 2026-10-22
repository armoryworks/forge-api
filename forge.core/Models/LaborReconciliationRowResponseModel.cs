namespace Forge.Core.Models;

public record LaborReconciliationRowResponseModel(
    int UserId,
    string UserName,
    DateOnly Date,
    decimal? PaidMinutes,
    decimal JobLaborMinutes,
    decimal IndirectMinutes,
    decimal ManualMinutes,
    decimal? UnallocatedMinutes,
    decimal OverlappingTimerMinutes,
    int LongTimers,
    int RunningTimers);
