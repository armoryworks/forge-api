namespace Forge.Core.Models;

public record ApprovalWorkflowResponseModel(
    int Id, string Name, string EntityType, bool IsActive,
    string? Description, string? ActivationConditionsJson,
    List<ApprovalStepResponseModel> Steps, DateTimeOffset CreatedAt);

public record ApprovalStepResponseModel(
    int Id, int StepNumber, string Name, string ApproverType,
    int? ApproverUserId, string? ApproverUserName, string? ApproverRole,
    bool UseDirectManager, decimal? AutoApproveBelow,
    int? EscalationHours, bool RequireComments, bool AllowDelegation);
