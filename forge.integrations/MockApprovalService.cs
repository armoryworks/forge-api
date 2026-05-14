using Microsoft.Extensions.Logging;

using Forge.Core.Entities;
using Forge.Core.Interfaces;

namespace Forge.Integrations;

public class MockApprovalService(ILogger<MockApprovalService> logger) : IApprovalService
{
    public Task<ApprovalRequest?> SubmitForApprovalAsync(string entityType, int entityId, int requestedById, decimal? amount, string? entitySummary, CancellationToken ct)
    {
        logger.LogInformation("MockApprovalService: SubmitForApproval {EntityType}:{EntityId} by user {UserId}, amount={Amount}",
            entityType, entityId, requestedById, amount);
        return Task.FromResult<ApprovalRequest?>(null);
    }

    public Task<ApprovalRequest> ApproveAsync(int requestId, int decidedById, string? comments, CancellationToken ct)
    {
        logger.LogInformation("MockApprovalService: Approve request {RequestId} by user {UserId}", requestId, decidedById);
        return Task.FromResult(new ApprovalRequest
        {
            Id = requestId,
            Status = ApprovalRequestStatus.Approved,
            CompletedAt = DateTimeOffset.UtcNow,
        });
    }

    public Task<ApprovalRequest> RejectAsync(int requestId, int decidedById, string comments, CancellationToken ct)
    {
        logger.LogInformation("MockApprovalService: Reject request {RequestId} by user {UserId}", requestId, decidedById);
        return Task.FromResult(new ApprovalRequest
        {
            Id = requestId,
            Status = ApprovalRequestStatus.Rejected,
            CompletedAt = DateTimeOffset.UtcNow,
        });
    }

    public Task<ApprovalRequest> DelegateAsync(int requestId, int decidedById, int delegateToUserId, string? comments, CancellationToken ct)
    {
        logger.LogInformation("MockApprovalService: Delegate request {RequestId} from user {UserId} to {DelegateToUserId}",
            requestId, decidedById, delegateToUserId);
        return Task.FromResult(new ApprovalRequest
        {
            Id = requestId,
            Status = ApprovalRequestStatus.Pending,
        });
    }

    public Task<bool> IsApprovalRequiredAsync(string entityType, decimal? amount, CancellationToken ct)
    {
        logger.LogInformation("MockApprovalService: IsApprovalRequired for {EntityType}, amount={Amount}", entityType, amount);
        return Task.FromResult(false);
    }

    public Task<IReadOnlyList<ApprovalRequest>> GetPendingApprovalsAsync(int userId, CancellationToken ct)
    {
        logger.LogInformation("MockApprovalService: GetPendingApprovals for user {UserId}", userId);
        return Task.FromResult<IReadOnlyList<ApprovalRequest>>(Array.Empty<ApprovalRequest>());
    }

    public Task CheckEscalationsAsync(CancellationToken ct)
    {
        logger.LogInformation("MockApprovalService: CheckEscalations — no-op in mock mode");
        return Task.CompletedTask;
    }
}
