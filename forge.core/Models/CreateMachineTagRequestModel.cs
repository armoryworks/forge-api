namespace Forge.Core.Models;

public record CreateMachineTagRequestModel
{
    public string TagName { get; init; } = string.Empty;
    public string OpcNodeId { get; init; } = string.Empty;
    public string DataType { get; init; } = string.Empty;
    public string? Unit { get; init; }
    public decimal? WarningThresholdLow { get; init; }
    public decimal? WarningThresholdHigh { get; init; }
    public decimal? AlarmThresholdLow { get; init; }
    public decimal? AlarmThresholdHigh { get; init; }
}
