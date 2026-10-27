using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Jobs.ProductionRuns;

public record GetProductionRunsQuery(int JobId) : IRequest<List<ProductionRunResponseModel>>;

public class GetProductionRunsHandler(AppDbContext db) : IRequestHandler<GetProductionRunsQuery, List<ProductionRunResponseModel>>
{
    public async Task<List<ProductionRunResponseModel>> Handle(GetProductionRunsQuery request, CancellationToken cancellationToken)
    {
        var runs = await db.ProductionRuns
            .AsNoTracking()
            .Where(pr => pr.JobId == request.JobId)
            .Include(pr => pr.Job)
            .Include(pr => pr.Part)
            .OrderBy(pr => pr.Id)
            .ToListAsync(cancellationToken);

        return await ProductionRunResponses.BuildManyAsync(db, runs, cancellationToken);
    }
}
