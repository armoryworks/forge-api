namespace Forge.Core.Models;

public record ShiftResponseModel(
    int Id,
    string Name,
    TimeOnly StartTime,
    TimeOnly EndTime,
    int BreakMinutes,
    decimal NetHours,
    bool IsActive);
