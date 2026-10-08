namespace Forge.Core.Models;

public record JobOperationSummaryResponseModel(
    int JobId,
    int OperationsTotal,
    int OperationsComplete,
    IReadOnlyList<int> InProgressSteps,
    int RunningTimerCount,
    decimal? EstimatedRemainingMinutes);
