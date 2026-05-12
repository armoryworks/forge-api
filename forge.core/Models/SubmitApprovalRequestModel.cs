namespace Forge.Core.Models;

public record SubmitApprovalRequestModel(
    string EntityType, int EntityId, decimal? Amount, string? EntitySummary);

public record ApprovalActionRequestModel(string? Comments);

public record DelegateApprovalRequestModel(int DelegateToUserId, string? Comments);
