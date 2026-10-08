namespace Forge.Core.Models;

public record JobOperationsResponseModel(
    int JobId,
    decimal JobQuantity,
    bool TrackingEnabled,
    bool AllOperationsComplete,
    decimal? EstimatedRemainingMinutes,
    DateTimeOffset ServerNow,
    List<JobOperationRowResponseModel> Operations);
