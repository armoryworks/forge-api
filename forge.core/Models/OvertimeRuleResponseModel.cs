namespace Forge.Core.Models;

public record OvertimeRuleResponseModel(
    int Id,
    string Name,
    decimal DailyThresholdHours,
    decimal WeeklyThresholdHours,
    decimal OvertimeMultiplier,
    decimal? DoubletimeThresholdDailyHours,
    decimal? DoubletimeThresholdWeeklyHours,
    decimal DoubletimeMultiplier,
    bool IsDefault,
    bool ApplyDailyBeforeWeekly);
