using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Leave;

public record GetAllLeaveBalancesQuery(int? PolicyId = null) : IRequest<List<LeaveBalanceResponseModel>>;

public class GetAllLeaveBalancesHandler(AppDbContext db) : IRequestHandler<GetAllLeaveBalancesQuery, List<LeaveBalanceResponseModel>>
{
    public async Task<List<LeaveBalanceResponseModel>> Handle(GetAllLeaveBalancesQuery request, CancellationToken cancellationToken)
    {
        var query = db.LeaveBalances.AsNoTracking().Where(b => b.Policy.DeletedAt == null);
        if (request.PolicyId.HasValue)
            query = query.Where(b => b.PolicyId == request.PolicyId.Value);

        return await query
            .Join(db.Users, b => b.UserId, u => u.Id, (b, u) => new { b, u.LastName, u.FirstName })
            .OrderBy(x => x.LastName).ThenBy(x => x.FirstName).ThenBy(x => x.b.Policy.Name)
            .Select(x => new LeaveBalanceResponseModel(
                x.b.Id, x.b.UserId, x.b.PolicyId, x.b.Policy.Name,
                x.b.Balance, x.b.UsedThisYear, x.b.AccruedThisYear,
                x.b.LastAccrualDate, x.LastName + ", " + x.FirstName))
            .ToListAsync(cancellationToken);
    }
}
