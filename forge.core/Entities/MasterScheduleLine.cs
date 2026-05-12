namespace Forge.Core.Entities;

public class MasterScheduleLine : BaseEntity
{
    public int MasterScheduleId { get; set; }
    public int PartId { get; set; }
    public decimal Quantity { get; set; }
    public DateTimeOffset DueDate { get; set; }
    public string? Notes { get; set; }

    public MasterSchedule MasterSchedule { get; set; } = null!;
    public Part Part { get; set; } = null!;
}
