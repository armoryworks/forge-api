namespace Forge.Core.Models;

public record KioskNextOperationResponseModel(
    int OperationId,
    int StepNumber,
    string Title,
    int? WorkCenterId,
    string? WorkCenterName);
