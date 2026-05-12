namespace Forge.Core.Models;

public record UpdatePlanningCycleRequestModel(
    string? Name,
    DateTimeOffset? StartDate,
    DateTimeOffset? EndDate,
    string? Goals);
