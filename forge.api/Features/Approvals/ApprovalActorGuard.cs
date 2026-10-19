using Forge.Api.Middleware;
using Forge.Core.Enums;
using Forge.Core.Interfaces;

namespace Forge.Api.Features.Approvals;

public static class ApprovalActorGuard
{
    public static async Task EnsureCanActAsync(
        this IApprovalService approvalService, int requestId, int userId, CancellationToken ct)
    {
        var status = await approvalService.GetActorStatusAsync(requestId, userId, ct);
        switch (status)
        {
            case ApprovalActorStatus.NotCurrentApprover:
                throw new ForbiddenException("You are not an approver for the current step of this request.");
            case ApprovalActorStatus.OwnRequest:
                throw new ForbiddenException("You cannot approve, reject or delegate a request you submitted.");
        }
    }
}
