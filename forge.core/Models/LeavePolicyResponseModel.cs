namespace Forge.Core.Models;

public record LeavePolicyResponseModel(
    int Id,
    string Name,
    decimal AccrualRatePerPayPeriod,
    decimal? MaxBalance,
    decimal? CarryOverLimit,
    bool AccrueFromHireDate,
    int? WaitingPeriodDays,
    bool IsPaidLeave,
    bool IsActive);
