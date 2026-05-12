namespace Forge.Core.Entities;

public class MaintenanceLog : BaseEntity
{
    public int MaintenanceScheduleId { get; set; }
    public int PerformedById { get; set; }
    public DateTimeOffset PerformedAt { get; set; }
    public decimal? HoursAtService { get; set; }
    public string? Notes { get; set; }
    public decimal? Cost { get; set; }

    public MaintenanceSchedule Schedule { get; set; } = null!;
}
