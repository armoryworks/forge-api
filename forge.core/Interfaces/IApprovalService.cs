using Forge.Core.Entities;

namespace Forge.Core.Interfaces;

public interface IApprovalService
{
    Task<ApprovalRequest?> SubmitForApprovalAsync(string entityType, int entityId, int requestedById, decimal? amount, string? entitySummary, CancellationToken ct);
    Task<ApprovalRequest> ApproveAsync(int requestId, int decidedById, string? comments, CancellationToken ct);
    Task<ApprovalRequest> RejectAsync(int requestId, int decidedById, string comments, CancellationToken ct);
    Task<ApprovalRequest> DelegateAsync(int requestId, int decidedById, int delegateToUserId, string? comments, CancellationToken ct);
    Task<bool> IsApprovalRequiredAsync(string entityType, decimal? amount, CancellationToken ct);
    Task<IReadOnlyList<ApprovalRequest>> GetPendingApprovalsAsync(int userId, CancellationToken ct);
    Task CheckEscalationsAsync(CancellationToken ct);
}
