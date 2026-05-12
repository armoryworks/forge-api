namespace Forge.Core.Models;

public record PlanningCycleEntryResponseModel(
    int Id,
    int JobId,
    string JobNumber,
    string JobTitle,
    string? AssigneeName,
    string StageName,
    string? StageColor,
    string Priority,
    bool IsRolledOver,
    DateTimeOffset CommittedAt,
    DateTimeOffset? CompletedAt,
    int SortOrder);
