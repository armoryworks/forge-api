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
        return await db.ReviewCycles.AsNoTracking()
            .Where(c => c.DeletedAt == null)
            .OrderByDescending(c => c.StartDate)
            .Select(c => new ReviewCycleResponseModel(
                c.Id, c.Name, c.StartDate, c.EndDate,
                c.Status, c.Description, c.Reviews.Count))
            .ToListAsync(cancellationToken);
    }
}
