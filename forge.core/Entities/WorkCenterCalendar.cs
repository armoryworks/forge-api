namespace Forge.Core.Entities;

public class WorkCenterCalendar : BaseEntity
{
    public int WorkCenterId { get; set; }
    public DateOnly Date { get; set; }
    public decimal AvailableHours { get; set; }
    public string? Reason { get; set; }

    public WorkCenter WorkCenter { get; set; } = null!;
}
