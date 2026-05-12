namespace Forge.Core.Models;

public record PlanningCycleListItemModel(
    int Id,
    string Name,
    DateTimeOffset StartDate,
    DateTimeOffset EndDate,
    string Status,
    int TotalJobs,
    int CompletedJobs,
    DateTimeOffset CreatedAt);
