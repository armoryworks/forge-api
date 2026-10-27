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

public record AcknowledgeReviewCommand(int Id, AcknowledgeReviewRequestModel Request) : IRequest<PerformanceReviewResponseModel>;

/// <summary>
/// The employee signs off that they have read the manager review: ManagerSubmitted →
/// Acknowledged. Acknowledging records receipt, not agreement; the employee may add comments.
/// </summary>
public class AcknowledgeReviewHandler(AppDbContext db, IHttpContextAccessor http, IClock clock)
    : IRequestHandler<AcknowledgeReviewCommand, PerformanceReviewResponseModel>
{
    public async Task<PerformanceReviewResponseModel> Handle(AcknowledgeReviewCommand command, CancellationToken ct)
    {
        var user = http.HttpContext?.User;
        var callerId = CallerAccess.UserId(user);
        var review = await ReviewAccess.LoadReadableAsync(db, command.Id, user, ct);

        if (review.EmployeeId != callerId)
            throw new ForbiddenException("Only the employee can acknowledge their review.");
        if (ReviewProjection.Rank(review.Status) != 2)
            throw new InvalidOperationException(ReviewProjection.Rank(review.Status) < 2
                ? "The manager review has not been submitted yet."
                : "This review has already been acknowledged.");

        var comments = command.Request.Comments?.Trim();
        if (comments is { Length: > 2000 })
            throw new InvalidOperationException("Comments are limited to 2000 characters.");

        review.Status = ReviewStatus.Acknowledged;
        review.AcknowledgedAt = clock.UtcNow;
        review.AcknowledgementComments = string.IsNullOrEmpty(comments) ? null : comments;
        db.LogActivityAt("acknowledged", "Employee acknowledged the review", ("PerformanceReview", review.Id));
        await db.SaveChangesAsync(ct);

        return (await ReviewProjection.ToModelsAsync(db, [review], callerId, ReviewAccess.IsAdmin(user), ct))[0];
    }
}
