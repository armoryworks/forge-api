using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Reviews;

/// <summary>
/// Builds review models for a viewer. The employee does not see the reviewer's ratings and
/// comments until the manager review is submitted, and the reviewer does not see the
/// self-assessment until the employee submits it. Admins see everything.
/// </summary>
public static class ReviewProjection
{
    public static bool IsAtOrAfter(ReviewStatus status, ReviewStatus stage)
        => Rank(status) >= Rank(stage);

    public static int Rank(ReviewStatus status) => status switch
    {
        ReviewStatus.NotStarted => 0,
        ReviewStatus.SelfAssessment => 0,
        ReviewStatus.SelfSubmitted => 1,
        ReviewStatus.ManagerReview => 1,
        ReviewStatus.ManagerSubmitted => 2,
        ReviewStatus.Discussion => 2,
        ReviewStatus.Acknowledged => 3,
        ReviewStatus.Completed => 4,
        ReviewStatus.Closed => 4,
        _ => 0,
    };

    public static async Task<List<PerformanceReviewResponseModel>> ToModelsAsync(
        AppDbContext db, IReadOnlyList<PerformanceReview> reviews, int viewerId, bool viewerIsAdmin, CancellationToken ct)
    {
        var userIds = reviews.SelectMany(r => new[] { r.EmployeeId, r.ReviewerId }).Distinct().ToList();
        var names = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.LastName + ", " + u.FirstName, ct);

        return reviews.Select(r => ToModel(r, names, viewerId, viewerIsAdmin)).ToList();
    }

    public static PerformanceReviewResponseModel ToModel(
        PerformanceReview r, IReadOnlyDictionary<int, string> names, int viewerId, bool viewerIsAdmin)
    {
        var isSubject = r.EmployeeId == viewerId && !viewerIsAdmin;
        var isReviewerOnly = !viewerIsAdmin && !isSubject;
        var hideManager = isSubject && !IsAtOrAfter(r.Status, ReviewStatus.ManagerSubmitted);
        var hideSelf = isReviewerOnly && !IsAtOrAfter(r.Status, ReviewStatus.SelfSubmitted);

        return new PerformanceReviewResponseModel(
            r.Id, r.CycleId, r.Cycle.Name,
            r.EmployeeId, names.GetValueOrDefault(r.EmployeeId, string.Empty),
            r.ReviewerId, names.GetValueOrDefault(r.ReviewerId, string.Empty),
            r.Status,
            hideManager ? null : r.OverallRating,
            hideManager ? null : r.GoalsJson,
            hideManager ? null : r.CompetenciesJson,
            hideManager ? null : r.StrengthsComments,
            hideManager ? null : r.ImprovementComments,
            hideSelf ? null : r.EmployeeSelfAssessment,
            r.CompletedAt,
            r.AcknowledgedAt,
            hideSelf ? null : r.SelfRatingsJson,
            r.SelfSubmittedAt,
            r.ManagerSubmittedAt,
            r.AcknowledgementComments,
            r.Cycle.RatingScaleMax,
            ReviewJson.Competencies(r.Cycle.CompetenciesJson));
    }

    public static ReviewCycleResponseModel ToCycleModel(ReviewCycle c, IReadOnlyList<ReviewStatus> statuses)
        => new(
            c.Id, c.Name, c.StartDate, c.EndDate, c.Status, c.Description, statuses.Count,
            c.RatingScaleMax,
            ReviewJson.Competencies(c.CompetenciesJson),
            c.ScopeType,
            c.ScopeDepartment,
            ReviewJson.UserIds(c.ScopeUserIdsJson),
            c.LaunchedAt,
            c.ClosedAt,
            new ReviewCycleProgressModel(
                statuses.Count(s => Rank(s) == 0),
                statuses.Count(s => Rank(s) == 1),
                statuses.Count(s => Rank(s) == 2),
                statuses.Count(s => Rank(s) == 3),
                statuses.Count(s => Rank(s) == 4)));
}
