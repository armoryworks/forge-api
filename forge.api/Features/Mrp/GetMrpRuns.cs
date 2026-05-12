using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Mrp;

public record GetMrpRunsQuery : IRequest<List<MrpRunResponseModel>>;

public class GetMrpRunsHandler(AppDbContext db)
    : IRequestHandler<GetMrpRunsQuery, List<MrpRunResponseModel>>
{
    public async Task<List<MrpRunResponseModel>> Handle(GetMrpRunsQuery request, CancellationToken cancellationToken)
    {
        return await db.MrpRuns
            .AsNoTracking()
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new MrpRunResponseModel(
                r.Id,
                r.RunNumber,
                r.RunType,
                r.Status,
                r.IsSimulation,
                r.StartedAt,
                r.CompletedAt,
                r.PlanningHorizonDays,
                r.TotalDemandCount,
                r.TotalSupplyCount,
                r.PlannedOrderCount,
                r.ExceptionCount,
                r.ErrorMessage,
                r.InitiatedByUserId
            ))
            .ToListAsync(cancellationToken);
    }
}
