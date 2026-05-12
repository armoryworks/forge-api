namespace Forge.Core.Entities;

public class MachineTag : BaseEntity
{
    public int ConnectionId { get; set; }
    public string TagName { get; set; } = string.Empty;
    public string OpcNodeId { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public string? Unit { get; set; }
    public decimal? WarningThresholdLow { get; set; }
    public decimal? WarningThresholdHigh { get; set; }
    public decimal? AlarmThresholdLow { get; set; }
    public decimal? AlarmThresholdHigh { get; set; }
    public bool IsActive { get; set; } = true;

    public MachineConnection Connection { get; set; } = null!;
}
