using MediatR;
using Microsoft.AspNetCore.Http;

using Forge.Api.Authorization;
using Forge.Api.Features.Notifications;
using Forge.Api.Middleware;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Reviews;

public record SubmitManagerReviewCommand(int Id, SubmitManagerReviewRequestModel Request) : IRequest<PerformanceReviewResponseModel>;

/// <summary>
/// The reviewer (or an Admin) submits the manager review: SelfSubmitted → ManagerSubmitted.
/// The employee is notified to read and acknowledge it.
/// </summary>
public class SubmitManagerReviewHandler(AppDbContext db, IHttpContextAccessor http, IClock clock, IMediator mediator)
    : IRequestHandler<SubmitManagerReviewCommand, PerformanceReviewResponseModel>
{
    public async Task<PerformanceReviewResponseModel> Handle(SubmitManagerReviewCommand command, CancellationToken ct)
    {
        var user = http.HttpContext?.User;
        var callerId = CallerAccess.UserId(user);
        var isAdmin = ReviewAccess.IsAdmin(user);
        var review = await ReviewAccess.LoadReadableAsync(db, command.Id, user, ct);

        var isReviewer = review.ReviewerId == callerId && review.EmployeeId != callerId;
        if (!isAdmin && !isReviewer)
            throw new ForbiddenException("Only the assigned reviewer or an administrator can submit the manager review.");
        if (review.EmployeeId == callerId)
            throw new ForbiddenException("You cannot write the manager review of your own performance.");
        if (ReviewProjection.Rank(review.Status) != 1)
            throw new InvalidOperationException(ReviewProjection.Rank(review.Status) == 0
                ? "The employee has not submitted their self-review yet."
                : "The manager review has already been submitted.");

        var r = command.Request;
        var scale = review.Cycle.RatingScaleMax;
        if (r.OverallRating < 1 || r.OverallRating > scale)
            throw new InvalidOperationException($"The overall rating runs from 1 to {scale}.");
        ReviewJson.EnsureRatingsInRange(r.Ratings, scale);

        review.OverallRating = r.OverallRating;
        if (r.Ratings is not null)
            review.CompetenciesJson = ReviewJson.Write(r.Ratings);
        if (r.Goals is not null)
            review.GoalsJson = ReviewJson.Write(ReviewJson.GoalLines(r.Goals));
        if (r.StrengthsComments is not null)
            review.StrengthsComments = r.StrengthsComments.Trim();
        if (r.ImprovementComments is not null)
            review.ImprovementComments = r.ImprovementComments.Trim();

        review.Status = ReviewStatus.ManagerSubmitted;
        review.ManagerSubmittedAt = clock.UtcNow;
        db.LogActivityAt("manager-submitted", $"Manager review submitted, overall {r.OverallRating:0.#} of {scale}", ("PerformanceReview", review.Id));
        await db.SaveChangesAsync(ct);

        await mediator.Send(new CreateNotificationCommand(new CreateNotificationRequestModel(
            UserId: review.EmployeeId,
            Type: "review-ready",
            Severity: "info",
            Source: "system",
            Title: $"Your {review.Cycle.Name} review is ready",
            Message: "Read your manager's review and acknowledge it.",
            EntityType: "PerformanceReview",
            EntityId: review.Id,
            SenderId: callerId == 0 ? null : callerId)), ct);

        return (await ReviewProjection.ToModelsAsync(db, [review], callerId, isAdmin, ct))[0];
    }
}
