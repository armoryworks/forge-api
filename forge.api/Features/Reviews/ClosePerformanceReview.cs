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

public record ClosePerformanceReviewCommand(int Id) : IRequest<PerformanceReviewResponseModel>;

/// <summary>
/// Closes one review: the reviewer closes an acknowledged review; an Admin may close a review
/// at any stage (for example when the employee has left).
/// </summary>
public class ClosePerformanceReviewHandler(AppDbContext db, IHttpContextAccessor http, IClock clock)
    : IRequestHandler<ClosePerformanceReviewCommand, PerformanceReviewResponseModel>
{
    public async Task<PerformanceReviewResponseModel> Handle(ClosePerformanceReviewCommand command, CancellationToken ct)
    {
        var user = http.HttpContext?.User;
        var callerId = CallerAccess.UserId(user);
        var isAdmin = ReviewAccess.IsAdmin(user);
        var review = await ReviewAccess.LoadReadableAsync(db, command.Id, user, ct);

        var isReviewer = review.ReviewerId == callerId && review.EmployeeId != callerId;
        if (!isAdmin && !isReviewer)
            throw new ForbiddenException("Only the assigned reviewer or an administrator can close this review.");
        if (ReviewProjection.Rank(review.Status) == 4)
            throw new InvalidOperationException("This review is already closed.");
        if (!isAdmin && ReviewProjection.Rank(review.Status) != 3)
            throw new InvalidOperationException("The employee has not acknowledged this review yet.");

        var from = review.Status;
        review.Status = ReviewStatus.Closed;
        review.CompletedAt = clock.UtcNow;
        db.LogActivityAt("closed", $"Review closed at {from}", ("PerformanceReview", review.Id));
        await db.SaveChangesAsync(ct);

        return (await ReviewProjection.ToModelsAsync(db, [review], callerId, isAdmin, ct))[0];
    }
}
