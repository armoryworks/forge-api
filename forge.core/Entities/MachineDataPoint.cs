
namespace Forge.Core.Entities;

public class MachineDataPoint : BaseEntity
{
    public int TagId { get; set; }
    public int WorkCenterId { get; set; }
    public string Value { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public MachineDataQuality Quality { get; set; }

    public MachineTag Tag { get; set; } = null!;
}
