using System.Security.Claims;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Authorization;
using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Api.Features.Reviews;

/// <summary>
/// Who may see a performance review: the employee it is about, its reviewer, an Admin, or a
/// Manager on the employee's team. Everyone else gets 404, so a review's existence is not leaked.
/// </summary>
public static class ReviewAccess
{
    public static bool IsAdmin(ClaimsPrincipal? user) => user?.IsInRole("Admin") == true;

    public static async Task<bool> CanReadAsync(AppDbContext db, PerformanceReview review, ClaimsPrincipal? user, CancellationToken ct)
    {
        var callerId = CallerAccess.UserId(user);
        if (IsAdmin(user) || review.EmployeeId == callerId || review.ReviewerId == callerId)
            return true;
        if (user?.IsInRole("Manager") != true)
            return false;

        var teamId = await db.Users.Where(u => u.Id == callerId).Select(u => u.TeamId).FirstOrDefaultAsync(ct);
        return teamId is not null
            && await db.Users.AnyAsync(u => u.Id == review.EmployeeId && u.TeamId == teamId, ct);
    }

    public static async Task<PerformanceReview> LoadReadableAsync(AppDbContext db, int id, ClaimsPrincipal? user, CancellationToken ct)
    {
        var review = await db.PerformanceReviews
            .Include(r => r.Cycle)
            .FirstOrDefaultAsync(r => r.Id == id, ct);
        if (review is null || !await CanReadAsync(db, review, user, ct))
            throw new KeyNotFoundException($"Performance review {id} not found");
        return review;
    }
}
