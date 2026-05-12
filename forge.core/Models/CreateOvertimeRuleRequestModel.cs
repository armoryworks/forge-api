namespace Forge.Core.Models;

public record CreateOvertimeRuleRequestModel(
    string Name,
    decimal DailyThresholdHours,
    decimal WeeklyThresholdHours,
    decimal OvertimeMultiplier,
    decimal? DoubletimeThresholdDailyHours,
    decimal? DoubletimeThresholdWeeklyHours,
    decimal DoubletimeMultiplier,
    bool IsDefault,
    bool ApplyDailyBeforeWeekly);
