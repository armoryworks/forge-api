using MediatR;
using Microsoft.AspNetCore.Http;

using Forge.Api.Authorization;
using Forge.Api.Middleware;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Reviews;

public record SubmitSelfReviewCommand(int Id, SubmitSelfReviewRequestModel Request) : IRequest<PerformanceReviewResponseModel>;

/// <summary>The employee submits their self-review: NotStarted → SelfSubmitted.</summary>
public class SubmitSelfReviewHandler(AppDbContext db, IHttpContextAccessor http, IClock clock)
    : IRequestHandler<SubmitSelfReviewCommand, PerformanceReviewResponseModel>
{
    public async Task<PerformanceReviewResponseModel> Handle(SubmitSelfReviewCommand command, CancellationToken ct)
    {
        var user = http.HttpContext?.User;
        var callerId = CallerAccess.UserId(user);
        var review = await ReviewAccess.LoadReadableAsync(db, command.Id, user, ct);

        if (review.EmployeeId != callerId)
            throw new ForbiddenException("Only the employee can submit their self-review.");
        if (ReviewProjection.Rank(review.Status) != 0)
            throw new InvalidOperationException("The self-review has already been submitted.");
        if (review.Cycle.Status != ReviewCycleStatus.Active)
            throw new InvalidOperationException("This review cycle is not open.");

        ReviewJson.EnsureRatingsInRange(command.Request.Ratings, review.Cycle.RatingScaleMax);
        if (command.Request.Ratings is not null)
            review.SelfRatingsJson = ReviewJson.Write(command.Request.Ratings);
        if (command.Request.SelfAssessment is not null)
            review.EmployeeSelfAssessment = command.Request.SelfAssessment.Trim();

        review.Status = ReviewStatus.SelfSubmitted;
        review.SelfSubmittedAt = clock.UtcNow;
        db.LogActivityAt("self-submitted", "Self-review submitted", ("PerformanceReview", review.Id));
        await db.SaveChangesAsync(ct);

        return (await ReviewProjection.ToModelsAsync(db, [review], callerId, ReviewAccess.IsAdmin(user), ct))[0];
    }
}
