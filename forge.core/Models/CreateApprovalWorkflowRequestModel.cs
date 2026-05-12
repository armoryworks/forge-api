namespace Forge.Core.Models;

public record CreateApprovalWorkflowRequestModel(
    string Name, string EntityType, string? Description,
    string? ActivationConditionsJson,
    List<CreateApprovalStepRequestModel> Steps);

public record CreateApprovalStepRequestModel(
    int StepNumber, string Name, string ApproverType,
    int? ApproverUserId, string? ApproverRole,
    bool UseDirectManager, decimal? AutoApproveBelow,
    int? EscalationHours, bool RequireComments, bool AllowDelegation);
