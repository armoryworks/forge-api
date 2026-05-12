namespace Forge.Core.Entities;

public class MaintenanceSchedule : BaseAuditableEntity
{
    public int AssetId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int IntervalDays { get; set; }
    public decimal? IntervalHours { get; set; }
    public DateTimeOffset? LastPerformedAt { get; set; }
    public DateTimeOffset NextDueAt { get; set; }
    public bool IsActive { get; set; } = true;
    public int? MaintenanceJobId { get; set; }

    public Asset Asset { get; set; } = null!;
    public Job? MaintenanceJob { get; set; }
    public ICollection<MaintenanceLog> Logs { get; set; } = [];
}
