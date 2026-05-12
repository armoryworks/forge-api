using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Leave;

public record GetLeaveRequestsQuery(int? UserId = null, LeaveRequestStatus? Status = null) : IRequest<List<LeaveRequestResponseModel>>;

public class GetLeaveRequestsHandler(AppDbContext db) : IRequestHandler<GetLeaveRequestsQuery, List<LeaveRequestResponseModel>>
{
    public async Task<List<LeaveRequestResponseModel>> Handle(GetLeaveRequestsQuery request, CancellationToken cancellationToken)
    {
        var query = db.LeaveRequests.AsNoTracking()
            .Include(r => r.Policy)
            .Where(r => r.DeletedAt == null)
            .AsQueryable();

        if (request.UserId.HasValue)
            query = query.Where(r => r.UserId == request.UserId.Value);

        if (request.Status.HasValue)
            query = query.Where(r => r.Status == request.Status.Value);

        return await query
            .OrderByDescending(r => r.CreatedAt)
            .Join(db.Users, r => r.UserId, u => u.Id, (r, u) => new { r, UserName = u.LastName + ", " + u.FirstName })
            .GroupJoin(db.Users, x => x.r.ApprovedById, u => u.Id, (x, approvers) => new { x.r, x.UserName, Approver = approvers.FirstOrDefault() })
            .Select(x => new LeaveRequestResponseModel(
                x.r.Id, x.r.UserId, x.UserName,
                x.r.PolicyId, x.r.Policy.Name,
                x.r.StartDate, x.r.EndDate, x.r.Hours,
                x.r.Status, x.r.ApprovedById,
                x.Approver != null ? x.Approver.LastName + ", " + x.Approver.FirstName : null,
                x.r.DecidedAt, x.r.Reason, x.r.DenialReason,
                x.r.CreatedAt))
            .ToListAsync(cancellationToken);
    }
}
