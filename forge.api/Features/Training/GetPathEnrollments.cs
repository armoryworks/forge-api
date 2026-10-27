using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Training;

public record GetPathEnrollmentsQuery(int PathId) : IRequest<List<TrainingPathEnrollmentResponseModel>>;

public class GetPathEnrollmentsHandler(AppDbContext db, IClock clock)
    : IRequestHandler<GetPathEnrollmentsQuery, List<TrainingPathEnrollmentResponseModel>>
{
    public async Task<List<TrainingPathEnrollmentResponseModel>> Handle(GetPathEnrollmentsQuery request, CancellationToken ct)
    {
        var path = await db.TrainingPaths.AsNoTracking()
            .Include(p => p.PathModules)
            .FirstOrDefaultAsync(p => p.Id == request.PathId, ct)
            ?? throw new KeyNotFoundException($"Training path {request.PathId} not found.");
        var moduleIds = path.PathModules.Select(pm => pm.ModuleId).Distinct().ToList();

        var enrollments = await db.TrainingPathEnrollments.AsNoTracking()
            .Where(e => e.PathId == path.Id)
            .ToListAsync(ct);
        var userIds = enrollments.Select(e => e.UserId)
            .Concat(enrollments.Where(e => e.AssignedByUserId != null).Select(e => e.AssignedByUserId!.Value))
            .Distinct()
            .ToList();
        var users = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, Name = u.LastName + ", " + u.FirstName, u.IsActive })
            .ToDictionaryAsync(u => u.Id, ct);
        var enrolledIds = enrollments.Select(e => e.UserId).ToList();
        var completed = (await db.TrainingProgress.AsNoTracking()
                .Where(p => enrolledIds.Contains(p.UserId) && moduleIds.Contains(p.ModuleId) && p.Status == TrainingProgressStatus.Completed)
                .Select(p => new { p.UserId, p.ModuleId })
                .ToListAsync(ct))
            .GroupBy(p => p.UserId)
            .ToDictionary(g => g.Key, g => g.Select(p => p.ModuleId).Distinct().Count());

        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        return enrollments
            .Select(e =>
            {
                var done = completed.GetValueOrDefault(e.UserId);
                var user = users.GetValueOrDefault(e.UserId);
                var finished = e.CompletedAt is not null || (moduleIds.Count > 0 && done >= moduleIds.Count);
                return new TrainingPathEnrollmentResponseModel(
                    e.Id,
                    e.UserId,
                    user?.Name ?? string.Empty,
                    user?.IsActive ?? false,
                    e.CreatedAt,
                    e.DueDate,
                    !finished && e.DueDate is DateOnly due && due < today,
                    e.IsAutoAssigned,
                    e.AssignedByUserId is int by ? users.GetValueOrDefault(by)?.Name : null,
                    moduleIds.Count,
                    done,
                    moduleIds.Count > 0 ? Math.Round((double)done / moduleIds.Count * 100, 1) : 0,
                    e.CompletedAt);
            })
            .OrderBy(e => e.UserName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
