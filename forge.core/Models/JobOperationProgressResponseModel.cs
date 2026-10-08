namespace Forge.Core.Models;

public record JobOperationProgressResponseModel(
    JobOperationRowResponseModel Operation,
    bool AllOperationsComplete,
    decimal? EstimatedRemainingMinutes);
