namespace Forge.Core.Models;

public record ApprovalRequestResponseModel(
    int Id, string WorkflowName, string EntityType, int EntityId,
    string? EntitySummary, decimal? Amount,
    int CurrentStepNumber, string? CurrentStepName,
    string Status, string RequestedByName,
    DateTimeOffset RequestedAt, DateTimeOffset? CompletedAt,
    List<ApprovalDecisionResponseModel> Decisions);

public record ApprovalDecisionResponseModel(
    int Id, int StepNumber, string StepName,
    string DecidedByName, string Decision,
    string? Comments, DateTimeOffset DecidedAt,
    string? DelegatedToUserName);
