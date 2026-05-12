namespace Forge.Core.Entities;

public class OvertimeRule : BaseAuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public decimal DailyThresholdHours { get; set; } = 8;
    public decimal WeeklyThresholdHours { get; set; } = 40;
    public decimal OvertimeMultiplier { get; set; } = 1.5m;
    public decimal? DoubletimeThresholdDailyHours { get; set; } = 12;
    public decimal? DoubletimeThresholdWeeklyHours { get; set; }
    public decimal DoubletimeMultiplier { get; set; } = 2.0m;
    public bool IsDefault { get; set; }
    public bool ApplyDailyBeforeWeekly { get; set; } = true;
}
