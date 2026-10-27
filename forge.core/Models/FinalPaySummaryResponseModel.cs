namespace Forge.Core.Models;

public record FinalPaySummaryResponseModel(
    int EmployeeProfileId,
    DateOnly? TerminationDate,
    bool FinalPayRequired,
    DateTimeOffset? FinalPayProcessedAt,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal HoursSinceLastPeriod,
    decimal PtoPayoutHours,
    decimal? HourlyRate,
    decimal? PtoPayoutAmount,
    IReadOnlyList<FinalPayLeaveLineModel> LeaveLines);
