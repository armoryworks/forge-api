using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Authorization;
using Forge.Api.Middleware;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

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

public class UpdatePerformanceReviewHandler(AppDbContext db, IHttpContextAccessor http) : IRequestHandler<UpdatePerformanceReviewCommand, PerformanceReviewResponseModel>
{
    public async Task<PerformanceReviewResponseModel> Handle(UpdatePerformanceReviewCommand request, CancellationToken cancellationToken)
    {
        var review = await db.PerformanceReviews
            .Include(r => r.Cycle)
            .FirstOrDefaultAsync(r => r.Id == request.Id && r.DeletedAt == null, cancellationToken)
            ?? throw new KeyNotFoundException($"Performance review {request.Id} not found");

        EnsureCallerMayWrite(review.EmployeeId, review.ReviewerId, request);
        EnsureStageOpen(review.Status, request);
        if (request.OverallRating is decimal rating && (rating < 1 || rating > review.Cycle.RatingScaleMax))
            throw new InvalidOperationException($"The overall rating runs from 1 to {review.Cycle.RatingScaleMax}.");

        if (request.OverallRating.HasValue) review.OverallRating = request.OverallRating.Value;
        if (request.GoalsJson != null) review.GoalsJson = request.GoalsJson;
        if (request.CompetenciesJson != null) review.CompetenciesJson = request.CompetenciesJson;
        if (request.StrengthsComments != null) review.StrengthsComments = request.StrengthsComments;
        if (request.ImprovementComments != null) review.ImprovementComments = request.ImprovementComments;
        if (request.EmployeeSelfAssessment != null) review.EmployeeSelfAssessment = request.EmployeeSelfAssessment;

        db.LogActivityAt("draft-saved", "Draft saved", ("PerformanceReview", review.Id));
        await db.SaveChangesAsync(cancellationToken);

        var user = http.HttpContext?.User;
        return (await ReviewProjection.ToModelsAsync(
            db, [review], CallerAccess.UserId(user), user?.IsInRole("Admin") == true, cancellationToken))[0];
    }

    private static void EnsureStageOpen(ReviewStatus status, UpdatePerformanceReviewCommand request)
    {
        if (request.Status.HasValue)
            throw new InvalidOperationException("A review moves through its stages with the submit, acknowledge and close actions, not by setting its status.");

        if (request.EmployeeSelfAssessment != null && ReviewProjection.Rank(status) != 0)
            throw new InvalidOperationException("The self-review has been submitted and can no longer be edited.");

        var writesReviewerFields = request.OverallRating.HasValue
            || request.GoalsJson != null
            || request.CompetenciesJson != null
            || request.StrengthsComments != null
            || request.ImprovementComments != null;
        if (writesReviewerFields && ReviewProjection.Rank(status) > 1)
            throw new InvalidOperationException("The manager review has been submitted and can no longer be edited.");
    }

    private void EnsureCallerMayWrite(int employeeId, int reviewerId, UpdatePerformanceReviewCommand request)
    {
        var user = http.HttpContext?.User;
        var callerId = CallerAccess.UserId(user);
        var isAdmin = user?.IsInRole("Admin") == true;
        var isSubject = employeeId == callerId;
        var isReviewer = reviewerId == callerId && !isSubject;

        if (!isAdmin && !isSubject && !isReviewer)
            throw new ForbiddenException("Only the employee, the assigned reviewer or an administrator can update this review.");

        var writesReviewerFields = request.Status.HasValue
            || request.OverallRating.HasValue
            || request.GoalsJson != null
            || request.CompetenciesJson != null
            || request.StrengthsComments != null
            || request.ImprovementComments != null;
        if (writesReviewerFields && !isAdmin && !isReviewer)
            throw new ForbiddenException("Only the assigned reviewer or an administrator can set the status, rating, goals or comments.");

        if (request.EmployeeSelfAssessment != null && !isAdmin && !isSubject)
            throw new ForbiddenException("Only the employee can write the self-assessment.");
    }
}
