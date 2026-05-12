using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Leave;

public record GetLeavePoliciesQuery(bool ActiveOnly = true) : IRequest<List<LeavePolicyResponseModel>>;

public class GetLeavePoliciesHandler(AppDbContext db) : IRequestHandler<GetLeavePoliciesQuery, List<LeavePolicyResponseModel>>
{
    public async Task<List<LeavePolicyResponseModel>> Handle(GetLeavePoliciesQuery request, CancellationToken cancellationToken)
    {
        var query = db.LeavePolicies.AsNoTracking().Where(p => p.DeletedAt == null);

        if (request.ActiveOnly)
            query = query.Where(p => p.IsActive);

        return await query
            .OrderBy(p => p.Name)
            .Select(p => new LeavePolicyResponseModel(
                p.Id, p.Name, p.AccrualRatePerPayPeriod,
                p.MaxBalance, p.CarryOverLimit,
                p.AccrueFromHireDate, p.WaitingPeriodDays,
                p.IsPaidLeave, p.IsActive))
            .ToListAsync(cancellationToken);
    }
}
