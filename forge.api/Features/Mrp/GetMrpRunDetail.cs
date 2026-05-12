using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Mrp;

public record GetMrpRunDetailQuery(int Id) : IRequest<MrpRunResponseModel>;

public class GetMrpRunDetailHandler(AppDbContext db)
    : IRequestHandler<GetMrpRunDetailQuery, MrpRunResponseModel>
{
    public async Task<MrpRunResponseModel> Handle(GetMrpRunDetailQuery request, CancellationToken cancellationToken)
    {
        var run = await db.MrpRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"MRP run {request.Id} not found.");

        return new MrpRunResponseModel(
            run.Id,
            run.RunNumber,
            run.RunType,
            run.Status,
            run.IsSimulation,
            run.StartedAt,
            run.CompletedAt,
            run.PlanningHorizonDays,
            run.TotalDemandCount,
            run.TotalSupplyCount,
            run.PlannedOrderCount,
            run.ExceptionCount,
            run.ErrorMessage,
            run.InitiatedByUserId
        );
    }
}
