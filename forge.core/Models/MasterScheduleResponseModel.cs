
namespace Forge.Core.Models;

public record MasterScheduleResponseModel(
    int Id,
    string Name,
    string? Description,
    MasterScheduleStatus Status,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    int CreatedByUserId,
    DateTimeOffset CreatedAt,
    int LineCount
);
