using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Reviews;

public record GetPerformanceReviewsQuery(int? CycleId = null, int? EmployeeId = null) : IRequest<List<PerformanceReviewResponseModel>>;

public class GetPerformanceReviewsHandler(AppDbContext db) : IRequestHandler<GetPerformanceReviewsQuery, List<PerformanceReviewResponseModel>>
{
    public async Task<List<PerformanceReviewResponseModel>> Handle(GetPerformanceReviewsQuery request, CancellationToken cancellationToken)
    {
        var query = db.PerformanceReviews.AsNoTracking()
            .Include(r => r.Cycle)
            .Where(r => r.DeletedAt == null)
            .AsQueryable();

        if (request.CycleId.HasValue)
            query = query.Where(r => r.CycleId == request.CycleId.Value);

        if (request.EmployeeId.HasValue)
            query = query.Where(r => r.EmployeeId == request.EmployeeId.Value);

        return await query
            .OrderBy(r => r.Cycle.StartDate)
            .Join(db.Users, r => r.EmployeeId, u => u.Id, (r, emp) => new { r, EmpName = emp.LastName + ", " + emp.FirstName })
            .Join(db.Users, x => x.r.ReviewerId, u => u.Id, (x, rev) => new { x.r, x.EmpName, RevName = rev.LastName + ", " + rev.FirstName })
            .Select(x => new PerformanceReviewResponseModel(
                x.r.Id, x.r.CycleId, x.r.Cycle.Name,
                x.r.EmployeeId, x.EmpName,
                x.r.ReviewerId, x.RevName,
                x.r.Status, x.r.OverallRating,
                x.r.GoalsJson, x.r.CompetenciesJson,
                x.r.StrengthsComments, x.r.ImprovementComments,
                x.r.EmployeeSelfAssessment,
                x.r.CompletedAt, x.r.AcknowledgedAt))
            .ToListAsync(cancellationToken);
    }
}
