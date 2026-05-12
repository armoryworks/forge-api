namespace Forge.Core.Entities;

public class TimeCorrectionLog : BaseAuditableEntity
{
    public int TimeEntryId { get; set; }
    public int CorrectedByUserId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public int? OriginalJobId { get; set; }
    public DateOnly OriginalDate { get; set; }
    public int OriginalDurationMinutes { get; set; }
    public DateTimeOffset? OriginalStartTime { get; set; }
    public DateTimeOffset? OriginalEndTime { get; set; }
    public string? OriginalCategory { get; set; }
    public string? OriginalNotes { get; set; }

    // Navigation
    public TimeEntry TimeEntry { get; set; } = null!;
}
