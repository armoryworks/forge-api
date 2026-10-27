namespace Forge.Api.Features.Approvals;

/// <summary>
/// Thrown when an approval is requested for a record that already has an approval request in progress
/// (Pending or Escalated). Answered with 409 <c>approval-already-pending</c>.
/// </summary>
public class ApprovalAlreadyPendingException(string entityType, int entityId, int requestId)
    : Exception($"{entityType} {entityId} already has an approval request in progress.")
{
    public const string Code = "approval-already-pending";

    public string EntityType { get; } = entityType;

    public int EntityId { get; } = entityId;

    public int RequestId { get; } = requestId;
}
