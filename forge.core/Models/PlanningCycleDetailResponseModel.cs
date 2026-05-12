namespace Forge.Core.Models;

public record PlanningCycleDetailResponseModel(
    int Id,
    string Name,
    DateTimeOffset StartDate,
    DateTimeOffset EndDate,
    string? Goals,
    string Status,
    int DurationDays,
    List<PlanningCycleEntryResponseModel> Entries,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
