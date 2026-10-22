using Forge.Core.Enums;

namespace Forge.Core.Models;

public record JobOperationsResponseModel(
    int JobId,
    decimal JobQuantity,
    bool TrackingEnabled,
    bool AllOperationsComplete,
    decimal? EstimatedRemainingMinutes,
    DateTimeOffset ServerNow,
    List<JobOperationRowResponseModel> Operations,
    decimal? TargetQuantity = null,
    int? QuantityDecimalPlaces = null,
    StepGatingMode StepGating = StepGatingMode.Off);
