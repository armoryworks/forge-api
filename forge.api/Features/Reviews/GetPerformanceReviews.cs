using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Authorization;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Reviews;

public record GetPerformanceReviewsQuery(int? CycleId = null, int? EmployeeId = null, string? View = null)
    : IRequest<List<PerformanceReviewResponseModel>>;

/// <summary>
/// Lists reviews the caller may see. <c>View = "mine"</c> returns the caller's own reviews,
/// <c>"team"</c> the reviews the caller is the reviewer of; otherwise Admins see every review and
/// Managers the reviews they write plus those of employees on their team.
/// </summary>
public class GetPerformanceReviewsHandler(AppDbContext db, IHttpContextAccessor http)
    : IRequestHandler<GetPerformanceReviewsQuery, List<PerformanceReviewResponseModel>>
{
    public const string ViewMine = "mine";
    public const string ViewTeam = "team";

    public async Task<List<PerformanceReviewResponseModel>> Handle(GetPerformanceReviewsQuery request, CancellationToken cancellationToken)
    {
        var user = http.HttpContext?.User;
        var callerId = CallerAccess.UserId(user);
        var isAdmin = ReviewAccess.IsAdmin(user);

        var query = db.PerformanceReviews.AsNoTracking()
            .Include(r => r.Cycle)
            .AsQueryable();

        if (request.View == ViewMine)
        {
            query = query.Where(r => r.EmployeeId == callerId);
        }
        else if (request.View == ViewTeam)
        {
            query = query.Where(r => r.ReviewerId == callerId && r.EmployeeId != callerId);
        }
        else if (!isAdmin)
        {
            var teamId = await db.Users.Where(u => u.Id == callerId).Select(u => u.TeamId).FirstOrDefaultAsync(cancellationToken);
            query = query.Where(r => r.ReviewerId == callerId
                || (teamId != null && db.Users.Any(u => u.Id == r.EmployeeId && u.TeamId == teamId)));
        }

        if (request.CycleId.HasValue)
            query = query.Where(r => r.CycleId == request.CycleId.Value);

        if (request.EmployeeId.HasValue)
            query = query.Where(r => r.EmployeeId == request.EmployeeId.Value);

        var reviews = await query
            .OrderByDescending(r => r.Cycle.StartDate)
            .ThenBy(r => r.Id)
            .ToListAsync(cancellationToken);

        return await ReviewProjection.ToModelsAsync(db, reviews, callerId, isAdmin, cancellationToken);
    }
}
