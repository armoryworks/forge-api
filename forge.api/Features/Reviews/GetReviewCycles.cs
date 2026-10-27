using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Reviews;

public record GetReviewCyclesQuery : IRequest<List<ReviewCycleResponseModel>>;

public class GetReviewCyclesHandler(AppDbContext db) : IRequestHandler<GetReviewCyclesQuery, List<ReviewCycleResponseModel>>
{
    public async Task<List<ReviewCycleResponseModel>> Handle(GetReviewCyclesQuery request, CancellationToken cancellationToken)
    {
        var cycles = await db.ReviewCycles.AsNoTracking()
            .Where(c => c.DeletedAt == null)
            .OrderByDescending(c => c.StartDate)
            .ToListAsync(cancellationToken);
        var ids = cycles.Select(c => c.Id).ToList();
        var statuses = (await db.PerformanceReviews.AsNoTracking()
                .Where(r => ids.Contains(r.CycleId))
                .Select(r => new { r.CycleId, r.Status })
                .ToListAsync(cancellationToken))
            .ToLookup(r => r.CycleId, r => r.Status);

        return cycles.Select(c => ReviewProjection.ToCycleModel(c, statuses[c.Id].ToList())).ToList();
    }
}
