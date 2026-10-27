namespace Forge.Core.Models;

public record OvertimeBreakdownResponseModel(
    decimal RegularHours,
    decimal OvertimeHours,
    decimal DoubletimeHours,
    decimal RegularCost,
    decimal OvertimeCost,
    decimal DoubletimeCost,
    decimal TotalCost,
    IReadOnlyList<DailyOvertimeDetailResponseModel> DailyBreakdown,
    string? RuleName = null,
    bool DailyThresholdsFirst = false,
    decimal DailyThresholdHours = 0,
    decimal WeeklyThresholdHours = 0);

public record DailyOvertimeDetailResponseModel(
    DateOnly Date,
    decimal TotalHours,
    decimal RegularHours,
    decimal OvertimeHours,
    decimal DoubletimeHours);
