using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Authorization;
using Forge.Api.Middleware;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Leave;

public record CancelLeaveRequestCommand(int Id) : IRequest;

public class CancelLeaveRequestHandler(AppDbContext db, IHttpContextAccessor http) : IRequestHandler<CancelLeaveRequestCommand>
{
    public async Task Handle(CancelLeaveRequestCommand request, CancellationToken cancellationToken)
    {
        var leaveRequest = await db.LeaveRequests.FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Leave request {request.Id} not found");

        var caller = http.HttpContext?.User;
        if (leaveRequest.UserId != CallerAccess.UserId(caller) && !CallerAccess.IsSupervisor(caller))
            throw new ForbiddenException("You can only cancel your own leave requests.");

        if (leaveRequest.Status != LeaveRequestStatus.Pending)
            throw new InvalidOperationException("Only pending requests can be cancelled.");

        leaveRequest.Status = LeaveRequestStatus.Cancelled;
        db.LogActivityAt("leave-request-cancelled",
            $"Cancelled leave request for {leaveRequest.StartDate:yyyy-MM-dd} to {leaveRequest.EndDate:yyyy-MM-dd}",
            ("LeaveRequest", leaveRequest.Id));
        await db.SaveChangesAsync(cancellationToken);
    }
}
