using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Reviews;

public record UpdatePerformanceReviewCommand(
    int Id,
    ReviewStatus? Status,
    decimal? OverallRating,
    string? GoalsJson,
    string? CompetenciesJson,
    string? StrengthsComments,
    string? ImprovementComments,
    string? EmployeeSelfAssessment) : IRequest<PerformanceReviewResponseModel>;

public class UpdatePerformanceReviewHandler(AppDbContext db, IClock clock) : IRequestHandler<UpdatePerformanceReviewCommand, PerformanceReviewResponseModel>
{
    public async Task<PerformanceReviewResponseModel> Handle(UpdatePerformanceReviewCommand request, CancellationToken cancellationToken)
    {
        var review = await db.PerformanceReviews
            .Include(r => r.Cycle)
            .FirstOrDefaultAsync(r => r.Id == request.Id && r.DeletedAt == null, cancellationToken)
            ?? throw new KeyNotFoundException($"Performance review {request.Id} not found");

        if (request.Status.HasValue) review.Status = request.Status.Value;
        if (request.OverallRating.HasValue) review.OverallRating = request.OverallRating.Value;
        if (request.GoalsJson != null) review.GoalsJson = request.GoalsJson;
        if (request.CompetenciesJson != null) review.CompetenciesJson = request.CompetenciesJson;
        if (request.StrengthsComments != null) review.StrengthsComments = request.StrengthsComments;
        if (request.ImprovementComments != null) review.ImprovementComments = request.ImprovementComments;
        if (request.EmployeeSelfAssessment != null) review.EmployeeSelfAssessment = request.EmployeeSelfAssessment;

        if (request.Status == ReviewStatus.Completed && review.CompletedAt == null)
            review.CompletedAt = clock.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        var empName = await db.Users.Where(u => u.Id == review.EmployeeId)
            .Select(u => u.LastName + ", " + u.FirstName).FirstAsync(cancellationToken);
        var revName = await db.Users.Where(u => u.Id == review.ReviewerId)
            .Select(u => u.LastName + ", " + u.FirstName).FirstAsync(cancellationToken);

        return new PerformanceReviewResponseModel(
            review.Id, review.CycleId, review.Cycle.Name,
            review.EmployeeId, empName,
            review.ReviewerId, revName,
            review.Status, review.OverallRating,
            review.GoalsJson, review.CompetenciesJson,
            review.StrengthsComments, review.ImprovementComments,
            review.EmployeeSelfAssessment,
            review.CompletedAt, review.AcknowledgedAt);
    }
}
