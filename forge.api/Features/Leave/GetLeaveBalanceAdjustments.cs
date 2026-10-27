using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Authorization;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Leave;

public record GetLeaveBalanceAdjustmentsQuery(int UserId) : IRequest<List<LeaveBalanceAdjustmentResponseModel>>;

public class GetLeaveBalanceAdjustmentsHandler(AppDbContext db, IHttpContextAccessor http)
    : IRequestHandler<GetLeaveBalanceAdjustmentsQuery, List<LeaveBalanceAdjustmentResponseModel>>
{
    public async Task<List<LeaveBalanceAdjustmentResponseModel>> Handle(GetLeaveBalanceAdjustmentsQuery request, CancellationToken cancellationToken)
    {
        var userId = CallerAccess.ScopeReadToSelf(
            http.HttpContext?.User, request.UserId, "You can only view your own leave balance history.");

        return await db.LeaveBalanceAdjustments.AsNoTracking()
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.EffectiveDate)
            .ThenByDescending(a => a.Id)
            .GroupJoin(db.Users, a => a.CreatedByUserId, u => u.Id, (a, by) => new { a, By = by.FirstOrDefault() })
            .Select(x => new LeaveBalanceAdjustmentResponseModel(
                x.a.Id, x.a.UserId, x.a.PolicyId, x.a.Policy.Name, x.a.AdjustmentType,
                x.a.Hours, x.a.EffectiveDate, x.a.Reason, x.a.BalanceAfter,
                x.a.CreatedByUserId,
                x.By != null ? x.By.LastName + ", " + x.By.FirstName : null,
                x.a.CreatedAt))
            .ToListAsync(cancellationToken);
    }
}
