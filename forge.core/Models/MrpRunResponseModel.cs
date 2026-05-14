
namespace Forge.Core.Models;

public record MrpRunResponseModel(
    int Id,
    string RunNumber,
    MrpRunType RunType,
    MrpRunStatus Status,
    bool IsSimulation,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    int PlanningHorizonDays,
    int TotalDemandCount,
    int TotalSupplyCount,
    int PlannedOrderCount,
    int ExceptionCount,
    string? ErrorMessage,
    int? InitiatedByUserId
);
