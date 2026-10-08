using Forge.Core.Enums;

namespace Forge.Core.Models;

public record RaiseKioskAndonResponseModel
{
    public int AlertId { get; init; }
    public AndonAlertType Type { get; init; }
    public int JobId { get; init; }
    public string JobNumber { get; init; } = string.Empty;
    public int OperationId { get; init; }
    public int OperationStepNumber { get; init; }
    public string OperationTitle { get; init; } = string.Empty;
    public int WorkCenterId { get; init; }
    public string WorkCenterName { get; init; } = string.Empty;
}
