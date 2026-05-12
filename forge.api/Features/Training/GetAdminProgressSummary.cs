using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Training;

public record GetAdminProgressSummaryQuery : IRequest<List<TrainingAdminProgressSummaryResponseModel>>;

public class GetAdminProgressSummaryHandler(AppDbContext db, UserManager<ApplicationUser> userManager)
    : IRequestHandler<GetAdminProgressSummaryQuery, List<TrainingAdminProgressSummaryResponseModel>>
{
    public async Task<List<TrainingAdminProgressSummaryResponseModel>> Handle(
        GetAdminProgressSummaryQuery request, CancellationToken ct)
    {
        var users = await db.Users
            .AsNoTracking()
            .Where(u => u.IsActive)
            .OrderBy(u => u.LastName).ThenBy(u => u.FirstName)
            .ToListAsync(ct);

        var enrollments = await db.TrainingPathEnrollments
            .AsNoTracking()
            .Include(e => e.Path)
                .ThenInclude(p => p.PathModules)
            .ToListAsync(ct);

        var allProgress = await db.TrainingProgress
            .AsNoTracking()
            .ToListAsync(ct);

        var enrollmentsByUser = enrollments
            .GroupBy(e => e.UserId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var progressByUser = allProgress
            .GroupBy(p => p.UserId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var result = new List<TrainingAdminProgressSummaryResponseModel>();

        foreach (var user in users)
        {
            var roles = await userManager.GetRolesAsync(user);
            var role = roles.FirstOrDefault() ?? "Engineer";

            enrollmentsByUser.TryGetValue(user.Id, out var userEnrollments);
            progressByUser.TryGetValue(user.Id, out var userProgress);

            userEnrollments ??= [];
            userProgress ??= [];

            var totalEnrolled = userEnrollments.Count;

            var totalModulesAcrossAllPaths = userEnrollments
                .SelectMany(e => e.Path.PathModules)
                .Select(pm => pm.ModuleId)
                .Distinct()
                .Count();

            var completedModuleIds = userProgress
                .Where(p => p.Status == TrainingProgressStatus.Completed)
                .Select(p => p.ModuleId)
                .ToHashSet();

            var completedModulesInPaths = userEnrollments
                .SelectMany(e => e.Path.PathModules)
                .Select(pm => pm.ModuleId)
                .Distinct()
                .Count(id => completedModuleIds.Contains(id));

            var totalCompleted = userEnrollments.Count(e => e.CompletedAt.HasValue);

            var overallPct = totalModulesAcrossAllPaths > 0
                ? Math.Round((double)completedModulesInPaths / totalModulesAcrossAllPaths * 100, 1)
                : 0.0;

            var lastActivityAt = userProgress.Count > 0
                ? userProgress.Max(p => p.UpdatedAt)
                : (DateTimeOffset?)null;

            result.Add(new TrainingAdminProgressSummaryResponseModel(
                user.Id,
                $"{user.LastName}, {user.FirstName}",
                role,
                totalEnrolled,
                totalCompleted,
                totalModulesAcrossAllPaths,
                overallPct,
                lastActivityAt
            ));
        }

        return result;
    }
}
