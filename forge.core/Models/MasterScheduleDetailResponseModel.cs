
namespace Forge.Core.Models;

public record MasterScheduleDetailResponseModel(
    int Id,
    string Name,
    string? Description,
    MasterScheduleStatus Status,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    int CreatedByUserId,
    DateTimeOffset CreatedAt,
    List<MasterScheduleLineResponseModel> Lines
);
