namespace Forge.Core.Models;

public record OvertimeBreakdownResponseModel(
    decimal RegularHours,
    decimal OvertimeHours,
    decimal DoubletimeHours,
    decimal RegularCost,
    decimal OvertimeCost,
    decimal DoubletimeCost,
    decimal TotalCost,
    IReadOnlyList<DailyOvertimeDetailResponseModel> DailyBreakdown);

public record DailyOvertimeDetailResponseModel(
    DateOnly Date,
    decimal TotalHours,
    decimal RegularHours,
    decimal OvertimeHours,
    decimal DoubletimeHours);
