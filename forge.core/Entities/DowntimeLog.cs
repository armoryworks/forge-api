
namespace Forge.Core.Entities;

public class DowntimeLog : BaseAuditableEntity
{
    public int AssetId { get; set; }
    public int? WorkCenterId { get; set; }
    public int? ReportedById { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public DowntimeCategory? Category { get; set; }
    public int? DowntimeReasonId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? Resolution { get; set; }
    public string? Description { get; set; }
    public bool IsPlanned { get; set; }
    public int? JobId { get; set; }
    public string? Notes { get; set; }

    public decimal DurationMinutes => EndedAt.HasValue
        ? (decimal)(EndedAt.Value - StartedAt).TotalMinutes
        : (decimal)(DateTimeOffset.UtcNow - StartedAt).TotalMinutes;

    public decimal DurationHours => EndedAt.HasValue
        ? (decimal)(EndedAt.Value - StartedAt).TotalHours
        : (decimal)(DateTimeOffset.UtcNow - StartedAt).TotalHours;

    public Asset Asset { get; set; } = null!;
    public WorkCenter? WorkCenter { get; set; }
    public Job? Job { get; set; }
}
