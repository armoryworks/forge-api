using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Notifications;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Reviews;

public record LaunchReviewCycleCommand(int Id, int LaunchedByUserId) : IRequest<LaunchReviewCycleResponseModel>;

/// <summary>
/// Launches a draft cycle: one review per active employee in scope (everyone, one department,
/// or a chosen list), each reviewed by an active Manager on the employee's team, or by the
/// launching Admin when the team has none. Employees are notified that their self-review is open.
/// Relaunching an active cycle adds reviews for employees who joined the scope since.
/// </summary>
public class LaunchReviewCycleHandler(AppDbContext db, IMediator mediator, IClock clock)
    : IRequestHandler<LaunchReviewCycleCommand, LaunchReviewCycleResponseModel>
{
    public async Task<LaunchReviewCycleResponseModel> Handle(LaunchReviewCycleCommand request, CancellationToken ct)
    {
        var cycle = await db.ReviewCycles.FirstOrDefaultAsync(c => c.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Review cycle {request.Id} not found");
        if (cycle.Status == ReviewCycleStatus.Completed)
            throw new InvalidOperationException("A closed review cycle cannot be launched.");

        var employees = await InScopeAsync(cycle, ct);
        var existing = await db.PerformanceReviews.IgnoreQueryFilters()
            .Where(r => r.CycleId == cycle.Id)
            .Select(r => r.EmployeeId)
            .ToListAsync(ct);
        var toCreate = employees.Where(e => !existing.Contains(e.Id)).ToList();
        if (toCreate.Count == 0 && cycle.Status == ReviewCycleStatus.Draft)
            throw new InvalidOperationException("No active employees are in this cycle's scope.");

        var managers = await ManagersByTeamAsync(ct);
        var reviews = toCreate.Select(e => new PerformanceReview
        {
            CycleId = cycle.Id,
            EmployeeId = e.Id,
            ReviewerId = PickReviewer(e.Id, e.TeamId, managers, request.LaunchedByUserId),
        }).ToList();
        db.PerformanceReviews.AddRange(reviews);

        var firstLaunch = cycle.Status == ReviewCycleStatus.Draft;
        cycle.Status = ReviewCycleStatus.Active;
        cycle.LaunchedAt ??= clock.UtcNow;
        await db.SaveChangesAsync(ct);

        db.LogActivityAt(
            firstLaunch ? "launched" : "relaunched",
            $"{reviews.Count} review(s) created",
            ("ReviewCycle", cycle.Id));
        foreach (var review in reviews)
            db.LogActivityAt("created", $"Review opened in {cycle.Name}", ("PerformanceReview", review.Id));
        await db.SaveChangesAsync(ct);

        foreach (var review in reviews)
        {
            await mediator.Send(new CreateNotificationCommand(new CreateNotificationRequestModel(
                UserId: review.EmployeeId,
                Type: "review-open",
                Severity: "info",
                Source: "system",
                Title: $"Your {cycle.Name} review is open",
                Message: "Complete your self-review.",
                EntityType: "PerformanceReview",
                EntityId: review.Id,
                SenderId: request.LaunchedByUserId == 0 ? null : request.LaunchedByUserId)), ct);
        }

        return new LaunchReviewCycleResponseModel(cycle.Id, reviews.Count, existing.Count);
    }

    private async Task<List<(int Id, int? TeamId)>> InScopeAsync(ReviewCycle cycle, CancellationToken ct)
    {
        var users = db.Users.AsNoTracking()
            .Where(u => u.IsActive && !u.IsNonEmployee)
            .WithoutServiceAccounts(db);

        switch (cycle.ScopeType)
        {
            case ReviewScopeTypes.Department:
                var dept = cycle.ScopeDepartment ?? string.Empty;
                users = users.Where(u => db.EmployeeProfiles.Any(p => p.UserId == u.Id && p.Department == dept));
                break;
            case ReviewScopeTypes.List:
                var ids = ReviewJson.UserIds(cycle.ScopeUserIdsJson).ToList();
                users = users.Where(u => ids.Contains(u.Id));
                break;
        }

        return (await users.Select(u => new { u.Id, u.TeamId }).ToListAsync(ct))
            .Select(u => (u.Id, u.TeamId))
            .ToList();
    }

    private async Task<ILookup<int, int>> ManagersByTeamAsync(CancellationToken ct)
    {
        var managerRoleIds = db.Roles.Where(r => r.Name == "Manager").Select(r => r.Id);
        return (await db.Users.AsNoTracking()
                .Where(u => u.IsActive && u.TeamId != null)
                .Where(u => db.UserRoles.Any(ur => ur.UserId == u.Id && managerRoleIds.Contains(ur.RoleId)))
                .OrderBy(u => u.Id)
                .Select(u => new { u.Id, TeamId = u.TeamId!.Value })
                .ToListAsync(ct))
            .ToLookup(m => m.TeamId, m => m.Id);
    }

    private static int PickReviewer(int employeeId, int? teamId, ILookup<int, int> managers, int fallback)
    {
        if (teamId is int team)
        {
            var manager = managers[team].FirstOrDefault(m => m != employeeId);
            if (manager != 0)
                return manager;
        }

        return fallback;
    }
}
