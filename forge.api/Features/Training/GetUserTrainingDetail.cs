using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Training;

public record GetUserTrainingDetailQuery(int TargetUserId) : IRequest<UserTrainingDetailResponseModel>;

public class GetUserTrainingDetailHandler(AppDbContext db, UserManager<ApplicationUser> userManager)
    : IRequestHandler<GetUserTrainingDetailQuery, UserTrainingDetailResponseModel>
{
    public async Task<UserTrainingDetailResponseModel> Handle(
        GetUserTrainingDetailQuery request, CancellationToken ct)
    {
        var user = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == request.TargetUserId, ct)
            ?? throw new KeyNotFoundException($"User {request.TargetUserId} not found.");

        var roles = await userManager.GetRolesAsync(user);
        var role = roles.FirstOrDefault() ?? "Engineer";

        // Load all path enrollments with their path→modules
        var enrollments = await db.TrainingPathEnrollments
            .AsNoTracking()
            .Where(e => e.UserId == request.TargetUserId)
            .Include(e => e.Path)
                .ThenInclude(p => p.PathModules)
                    .ThenInclude(pm => pm.Module)
            .ToListAsync(ct);

        // All module IDs from enrolled paths (distinct)
        var pathModuleMap = enrollments
            .SelectMany(e => e.Path.PathModules)
            .Where(pm => pm.Module != null)
            .Select(pm => pm.Module!)
            .DistinctBy(m => m.Id)
            .ToDictionary(m => m.Id);

        // Load user progress for all their enrolled modules
        var progressRecords = await db.TrainingProgress
            .AsNoTracking()
            .Where(p => p.UserId == request.TargetUserId && pathModuleMap.Keys.Contains(p.ModuleId))
            .ToListAsync(ct);

        var progressByModule = progressRecords.ToDictionary(p => p.ModuleId);

        var completedModuleIds = progressRecords
            .Where(p => p.Status == TrainingProgressStatus.Completed)
            .Select(p => p.ModuleId)
            .ToHashSet();

        var totalModules = pathModuleMap.Count;
        var completedCount = pathModuleMap.Keys.Count(id => completedModuleIds.Contains(id));
        var overallPct = totalModules > 0
            ? Math.Round((double)completedCount / totalModules * 100, 1)
            : 0.0;

        // Build per-module details, sorted by completed (desc) then title
        var moduleDetails = pathModuleMap.Values
            .OrderBy(m => m.Title)
            .Select(m =>
            {
                progressByModule.TryGetValue(m.Id, out var p);
                return new UserTrainingModuleDetail(
                    m.Id,
                    m.Title,
                    m.ContentType.ToString(),
                    p?.Status.ToString(),
                    p?.QuizScore,
                    p?.QuizAttempts ?? 0,
                    p?.TimeSpentSeconds ?? 0,
                    p?.StartedAt,
                    p?.CompletedAt
                );
            })
            .ToList();

        return new UserTrainingDetailResponseModel(
            user.Id,
            $"{user.LastName}, {user.FirstName}",
            role,
            enrollments.Count,
            overallPct,
            moduleDetails
        );
    }
}
