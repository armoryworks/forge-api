namespace Forge.Core.Models;

public record CreateLeavePolicyRequestModel(
    string Name,
    decimal AccrualRatePerPayPeriod,
    decimal? MaxBalance,
    decimal? CarryOverLimit,
    bool AccrueFromHireDate,
    int? WaitingPeriodDays,
    bool IsPaidLeave);
