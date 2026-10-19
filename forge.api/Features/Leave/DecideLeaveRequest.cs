using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Middleware;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Leave;

public record DecideLeaveRequestCommand(int Id, bool Approve, int DecidedByUserId, string? DenialReason = null) : IRequest;

public class DecideLeaveRequestHandler(AppDbContext db, IClock clock) : IRequestHandler<DecideLeaveRequestCommand>
{
    public async Task Handle(DecideLeaveRequestCommand request, CancellationToken cancellationToken)
    {
        var leaveRequest = await db.LeaveRequests.FindAsync([request.Id], cancellationToken)
            ?? throw new KeyNotFoundException($"Leave request {request.Id} not found");

        if (leaveRequest.Status != LeaveRequestStatus.Pending)
            throw new InvalidOperationException("Only pending requests can be approved or denied");

        if (request.Approve)
        {
            if (leaveRequest.UserId == request.DecidedByUserId)
                throw new ForbiddenException("You cannot approve your own leave request. Another manager must approve it.");

            var policy = await db.LeavePolicies.FindAsync([leaveRequest.PolicyId], cancellationToken)
                ?? throw new KeyNotFoundException($"Leave policy {leaveRequest.PolicyId} not found");
            await LeaveBalanceRules.EnsureCoveredAsync(
                db, policy, leaveRequest.UserId, leaveRequest.Hours, leaveRequest.Id, countPending: false, cancellationToken);
        }

        leaveRequest.Status = request.Approve ? LeaveRequestStatus.Approved : LeaveRequestStatus.Denied;
        leaveRequest.ApprovedById = request.DecidedByUserId;
        leaveRequest.DecidedAt = clock.UtcNow;
        leaveRequest.DenialReason = request.DenialReason?.Trim();

        // Deduct from balance if approved
        if (request.Approve)
        {
            var balance = await db.LeaveBalances
                .FirstOrDefaultAsync(b => b.UserId == leaveRequest.UserId && b.PolicyId == leaveRequest.PolicyId, cancellationToken);

            if (balance != null)
            {
                balance.Balance -= leaveRequest.Hours;
                balance.UsedThisYear += leaveRequest.Hours;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
