using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Authorization;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Leave;

public record GetLeaveBalancesQuery(int UserId) : IRequest<List<LeaveBalanceResponseModel>>;

public class GetLeaveBalancesHandler(AppDbContext db, IHttpContextAccessor http) : IRequestHandler<GetLeaveBalancesQuery, List<LeaveBalanceResponseModel>>
{
    public async Task<List<LeaveBalanceResponseModel>> Handle(GetLeaveBalancesQuery request, CancellationToken cancellationToken)
    {
        var userId = CallerAccess.ScopeReadToSelf(
            http.HttpContext?.User, request.UserId, "You can only view your own leave balances.");

        return await db.LeaveBalances.AsNoTracking()
            .Include(b => b.Policy)
            .Where(b => b.UserId == userId)
            .OrderBy(b => b.Policy.Name)
            .Select(b => new LeaveBalanceResponseModel(
                b.Id, b.UserId, b.PolicyId, b.Policy.Name,
                b.Balance, b.UsedThisYear, b.AccruedThisYear,
                b.LastAccrualDate))
            .ToListAsync(cancellationToken);
    }
}
