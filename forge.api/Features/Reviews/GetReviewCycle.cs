using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Reviews;

public record GetReviewCycleQuery(int Id) : IRequest<ReviewCycleResponseModel>;

public class GetReviewCycleHandler(AppDbContext db) : IRequestHandler<GetReviewCycleQuery, ReviewCycleResponseModel>
{
    public async Task<ReviewCycleResponseModel> Handle(GetReviewCycleQuery request, CancellationToken ct)
    {
        var cycle = await db.ReviewCycles.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Review cycle {request.Id} not found");
        var statuses = await db.PerformanceReviews.AsNoTracking()
            .Where(r => r.CycleId == cycle.Id)
            .Select(r => r.Status)
            .ToListAsync(ct);
        return ReviewProjection.ToCycleModel(cycle, statuses);
    }
}
